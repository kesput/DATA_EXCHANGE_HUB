using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
var allowed = new[] {
 "SELECT 1", "-- UPDATE words are harmless\nSELECT 'DELETE; INSERT; ALTER; EXEC' AS Text",
 "SELECT N'It''s an UPDATE' AS [DELETE]", "/* outer /* DELETE */ end */ SELECT 1",
 "WITH c AS (SELECT 1 AS n) SELECT n FROM c", "SELECT COUNT(*) FROM dbo.T",
 "SELECT t.a FROM dbo.T t JOIN dbo.U u ON u.a=t.a WHERE EXISTS(SELECT 1 FROM dbo.V)",
 "SELECT 1 UNION ALL SELECT 2", "SELECT 1; SELECT 2", "SELECT [name] FROM [DB].[dbo].[T]",
 "SELECT * FROM [WM0].[SNTXDB].[dbo].[OWNER]", "SELECT CASE WHEN 1=1 THEN 'DROP' ELSE 'ALTER' END",
 "SELECT CAST(1 AS decimal(10,2)), COALESCE(NULL,1), GETDATE()",
 "SELECT ROW_NUMBER() OVER(ORDER BY id) FROM dbo.T", "SELECT TOP(10) * FROM dbo.T ORDER BY id",
 "CREATE TABLE #t(n int); INSERT #t VALUES(1); SELECT * FROM #t; DROP TABLE #t",
 "SELECT 1 AS n INTO #t; SELECT #t.n FROM #t; DROP TABLE #t",
 "SELECT 1 AS n INTO ##t; SELECT ##t.* FROM ##t; DROP TABLE ##t",
 "CREATE TABLE ##t(n int); INSERT ##t SELECT n FROM dbo.Permanent; UPDATE ##t SET n=n+1; DELETE ##t WHERE n=0; TRUNCATE TABLE ##t; DROP TABLE ##t",
 "IF OBJECT_ID('tempdb..#t') IS NOT NULL DROP TABLE #t; SELECT 1 AS n INTO #t; SELECT * FROM #t",
 "IF 1=1 BEGIN CREATE TABLE #t(n int); INSERT #t VALUES(1); END; SELECT * FROM #t",
 "DROP TABLE IF EXISTS #t; DROP TABLE IF EXISTS ##t"
};
foreach (var sql in allowed) { IrisSelectPolicy.Validate(sql); Check(true,"Allow SELECT: " + sql[..Math.Min(sql.Length,45)]); }
var denied = new[] {
 "DELETE FROM dbo.T", "UPDATE dbo.T SET a=1", "INSERT INTO dbo.T VALUES(1)",
 "ALTER TABLE dbo.T ADD a int", "DROP TABLE dbo.T", "TRUNCATE TABLE dbo.T", "CREATE TABLE dbo.T(a int)",
 "MERGE dbo.T AS t USING dbo.U AS u ON t.id=u.id WHEN MATCHED THEN DELETE;",
 "EXEC dbo.Report", "EXEC(N'SELECT 1; DELETE FROM dbo.T')", "SELECT 1; DELETE FROM dbo.T",
 "SELECT 1; /* hidden */ UPDATE dbo.T SET a=2", "SELECT 1 INTO dbo.NewTable",
 "SELECT NEXT VALUE FOR dbo.S", "WITH c AS(SELECT 1 AS n) DELETE FROM c",
 "SELECT * FROM OPENQUERY(remote,'DELETE FROM T')", "SELECT * FROM OPENROWSET('provider','connection','SELECT 1')",
 "SELECT * FROM OPENROWSET(BULK 'C:\\private.txt',SINGLE_CLOB) AS x",
 "SELECT * FROM OPENDATASOURCE('provider','connection').db.dbo.T",
 "SELECT dbo.CustomFunction()", "SELECT * FROM dbo.CustomTableFunction()",
 "DECLARE @x int; SELECT @x", "SELECT @x=1", "WAITFOR DELAY '00:00:01';SELECT 1",
 "SET NOCOUNT ON; SELECT 1", "DBCC CHECKDB", "GRANT SELECT ON dbo.T TO x",
 "USE master;SELECT 1", "SELECT 1\nGO\nDELETE dbo.T", "SELECT 1\nGO\nSELECT 2",
 "SELECT * FROM (DELETE dbo.T OUTPUT deleted.*) AS changes",
 "SELECT * FROM OPENROWSET(PROVIDER='CosmosDB',CONNECTION='Account=x;Database=x',OBJECT='x',SERVER_CREDENTIAL='x') AS x",
 "SELECT 1/ FROM dbo.T", "/* comment only */",
 "CREATE TABLE #t(n int); UPDATE dbo.Permanent SET n=1",
 "DROP TABLE #t, dbo.Permanent", "SELECT 1 INTO tempdb.dbo.Permanent",
 "DELETE FROM tempdb..##other", "SELECT * FROM tempdb..##other",
 "INSERT #t EXEC dbo.Report", "UPDATE #t SET n=1 OUTPUT inserted.n INTO dbo.Permanent",
 "IF 1=1 DROP TABLE dbo.Permanent", "BEGIN SELECT 1; DELETE dbo.Permanent; END"
};
foreach (var sql in denied) {
 bool rejected=false;try { IrisSelectPolicy.Validate(sql); } catch(InvalidOperationException) { rejected=true; }
 Check(rejected,"Reject unsafe SQL: " + sql[..Math.Min(sql.Length,45)]);
}
var env=new TestEnvironment {ContentRootPath=Directory.GetCurrentDirectory()};
var isolatedA=IrisSelectPolicy.Prepare("SELECT 1 AS n INTO ##shared; SELECT ##shared.n FROM ##shared; DROP TABLE ##shared");
var isolatedB=IrisSelectPolicy.Prepare("SELECT 1 AS n INTO ##shared; SELECT ##shared.n FROM ##shared; DROP TABLE ##shared");
Check(!isolatedA.Contains("##shared") && isolatedA.Contains("#iris_"),"Global temp names become private local names");
Check(isolatedA!=isolatedB,"Each preview gets a different temp namespace");
var emptyConfig=new ConfigurationBuilder().Build();var fake=new FakeAudit();
var options=Options.Create(new IrisOptions());
var sourceStore=new IrisService(emptyConfig,options);
try {await sourceStore.CreateSourceAsync(new IrisCreateSourceRequest(1,"INLINE_SQL","profile","","DELETE dbo.T","","","",true,false,false,@"TEST\owner"),default);throw new Exception("Unsafe source saved");}
catch(InvalidOperationException ex){Check(ex.Message=="Iris.Preview.SelectOnly","Source creation rejects unsafe SQL before database access");}
try {await sourceStore.UpdateSourceAsync(new IrisUpdateSourceRequest(1,1,"INLINE_SQL","profile","","UPDATE dbo.T SET n=1","","","",true,false,false),default);throw new Exception("Unsafe source updated");}
catch(InvalidOperationException ex){Check(ex.Message=="Iris.Preview.SelectOnly","Source update rejects unsafe SQL before database access");}
var service=new IrisSourcePreviewService(emptyConfig,options,env,fake);
var unsafeRequest=new IrisSourcePreviewRequest("INLINE_SQL","missing",null,"SELECT 1; DELETE dbo.T",null,null);
try { await service.PreviewAsync(unsafeRequest,@"TEST\owner",default); throw new Exception("Unsafe query accepted"); }
catch(InvalidOperationException ex) { Check(ex.Message=="Iris.Preview.SelectOnly","Reject before target connection is opened"); }
Check(fake.Actor==@"TEST\owner" && fake.Status=="REJECTED","Rejected SQL has actor and result in audit");
Check(fake.Sql==unsafeRequest.SqlText,"Exact rejected query is retained");
fake.FailStart=true;
try { await service.PreviewAsync(unsafeRequest with {SqlText="SELECT 1"},@"TEST\owner",default);throw new Exception("Audit bypass"); }
catch(InvalidOperationException ex) { Check(ex.Message=="Iris.Preview.AuditUnavailable","Audit failure prevents execution"); }
var source=new FakePreview();var http=new DefaultHttpContext();var metadata=new EmptyModelMetadataProvider();
var model=new DATA_EXCHANGE_HUB.Pages.Iris.IndexModel(null!,new TestUser(),null!,emptyConfig,source,NullLogger<DATA_EXCHANGE_HUB.Pages.Iris.IndexModel>.Instance) {
 PageContext=new PageContext {HttpContext=http,ViewData=new ViewDataDictionary(metadata,new ModelStateDictionary())},MetadataProvider=metadata,
 SourceSqlText="SELECT 7",SourceSqlDatabase="TargetDb",SourceSqlConnectionName="profile",SourceId=4,SourceReportId=5,SourceExcelGridlines=true
};
var partial=(PartialViewResult)await model.OnPostPreviewSourceAsync();
Check(partial.Model is IrisSourcePreviewView {Result:not null},"Unsaved source returns data partial");
Check(source.Actor==@"TEST\server-identity","Actor comes from server resolver");
Check(source.Request?.SqlText=="SELECT 7" && source.Request.Database=="TargetDb" && source.Request.SourceId==4 && source.Request.ReportId==5,"Form values and IDs reach preview");
Check(model.SourceExcelGridlines && model.SourceSqlText=="SELECT 7","Preview preserves unsaved settings");
Check(http.Response.Headers.CacheControl=="no-store","Result is not cached");
source.Fail=true;partial=(PartialViewResult)await model.OnPostPreviewSourceAsync();
Check(partial.Model is IrisSourcePreviewView {ErrorKey:"Iris.Preview.SelectOnly"},"Rejected SQL renders explanatory message");

if(args.Contains("--sql")) {
 var config=new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Local.json",true).Build();
 var targetBuilder=new SqlConnectionStringBuilder(config.GetConnectionString("DataExchangeSql")) {InitialCatalog="BLS_LOGIT_STAT"};
 var targetConfig=new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:PreviewTestSql"]=targetBuilder.ConnectionString }).Build();
 var suffix=Guid.NewGuid().ToString("N");
 var logName="IRIS_PREVIEW_TEST_LOG_"+suffix;var procName="IRIS_PREVIEW_TEST_PROC_"+suffix;
 var tableName="IRIS_PREVIEW_TEST_DATA_"+suffix;var globalName="IRIS_PREVIEW_TEST_GLOBAL_"+suffix;
 var sqlOptions=Options.Create(new IrisOptions {PreviewLogTableName="BLS_LOGIT_STAT.dbo."+logName,PreviewMaxRows=3,PreviewTimeoutSeconds=10});
 var store=new IrisPreviewAuditStore(targetConfig,sqlOptions,NullLogger<IrisPreviewAuditStore>.Instance);
 var testDirectory=Path.Combine(Path.GetTempPath(),"iris-preview-sql-"+suffix);Directory.CreateDirectory(Path.Combine(testDirectory,"Sql"));
 var sqlService=new IrisSourcePreviewService(targetConfig,sqlOptions,new TestEnvironment{ContentRootPath=testDirectory},store);
 await using var connection=new SqlConnection(targetBuilder.ConnectionString);await connection.OpenAsync();
 IrisSourcePreviewRequest Query(string sql)=>new("INLINE_SQL","PreviewTestSql",null,sql,null,null,4,5);
 async Task<object?> Scalar(string sql){await using var cmd=new SqlCommand(sql,connection);return await cmd.ExecuteScalarAsync();}
 try {
  await new SqlCommand($"CREATE TABLE dbo.[{tableName}](Id int NOT NULL); INSERT INTO dbo.[{tableName}] VALUES(1);",connection).ExecuteNonQueryAsync();
  await new SqlCommand($"CREATE PROCEDURE dbo.[{procName}] AS BEGIN SET NOCOUNT ON; SELECT CAST(42 AS int) AS FromProcedure; END",connection).ExecuteNonQueryAsync();
  var query="SELECT CAST(7 AS int) AS Qty, N'O''Reilly' AS TextValue, CAST(NULL AS date) AS EmptyValue, 0x0102 AS Bytes";
  var result=await sqlService.PreviewAsync(Query(query),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="7" && result.Tables[0].Rows[0][1]=="O'Reilly","Actual SELECT data returned");
  Check(result.Tables[0].Rows[0][2]=="NULL" && result.Tables[0].Rows[0][3]=="0x0102","NULL and binary data formatted");
  Check((string)(await Scalar($"SELECT SqlText FROM dbo.[{logName}] WHERE Id='{result.AuditId}'"))! ==query,"Exact SQL persisted in audit");
  Check((string)(await Scalar($"SELECT Actor FROM dbo.[{logName}] WHERE Id='{result.AuditId}'"))! ==@"TEST\iris-preview","Server actor persisted");
  Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM dbo.[{logName}] WHERE Id='{result.AuditId}' AND Status=N'SUCCESS' AND CompletedAt IS NOT NULL AND DurationMs IS NOT NULL AND RowsShown=1 AND SourceId=4 AND ReportId=5 AND DatabaseName=N'BLS_LOGIT_STAT'"))==1,"Success audit has timestamps, duration, IDs, database and rows");
  result=await sqlService.PreviewAsync(Query("SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4"),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows.Count==3 && result.Truncated,"Row limit enforced");
  result=await sqlService.PreviewAsync(Query("SELECT 1 AS n UNION ALL SELECT 2 UNION ALL SELECT 3"),@"TEST\iris-preview",default);
  Check(!result.Truncated && !result.Tables[0].Truncated,"Exact row limit does not falsely truncate");
  result=await sqlService.PreviewAsync(Query("SELECT 1 AS A;SELECT 2 AS B;SELECT 3 AS C;SELECT 4 AS D"),@"TEST\iris-preview",default);
  Check(result.Tables.Count==3 && result.Truncated,"Result table limit enforced");
  result=await sqlService.PreviewAsync(Query("SELECT REPLICATE(N'x',3000) AS TextValue"),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0].Length==2001 && result.Tables[0].Truncated,"Long cells are bounded");
  result=await sqlService.PreviewAsync(Query("SELECT 1 AS Empty WHERE 1=0"),@"TEST\iris-preview",default);
  Check(result.Tables.Count==1 && result.Tables[0].Rows.Count==0,"Empty result retains columns");
  result=await sqlService.PreviewAsync(Query("") with {SourceType="STORED_PROCEDURE",Procedure="dbo."+procName},@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="42","Developer-created procedure executes");
  Check(((string)(await Scalar($"SELECT SqlText FROM dbo.[{logName}] WHERE Id='{result.AuditId}'"))!).Contains(procName),"Procedure execution retained in audit");
  var file=Path.Combine(testDirectory,"Sql","report.sql");await File.WriteAllTextAsync(file,"SELECT 99 AS FromFile");
  result=await sqlService.PreviewAsync(Query("") with {SourceType="SQL_FILE",FilePath=file},@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="99","SQL file executes through SELECT policy");
  Check((string)(await Scalar($"SELECT SqlText FROM dbo.[{logName}] WHERE Id='{result.AuditId}'"))! =="SELECT 99 AS FromFile","SQL file contents audited");
  var tempScript="CREATE TABLE #t(n int); INSERT #t VALUES(1),(2); UPDATE #t SET n=n+10; DELETE #t WHERE n=12; SELECT #t.n FROM #t; DROP TABLE #t";
  result=await sqlService.PreviewAsync(Query(tempScript),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="11","Local temp create, insert, update, delete, select and drop execute");
  Check((string)(await Scalar($"SELECT SqlText FROM dbo.[{logName}] WHERE Id='{result.AuditId}'"))! ==tempScript,"Original temp script retained in audit");
  var executed=(string)(await Scalar($"SELECT ExecutedSql FROM dbo.[{logName}] WHERE Id='{result.AuditId}'"))!;
  Check(executed.Contains("#iris_") && executed!=tempScript,"Isolated executed script retained separately");
  result=await sqlService.PreviewAsync(Query("IF OBJECT_ID('tempdb..##t') IS NOT NULL DROP TABLE ##t; SELECT 8 AS n INTO ##t; SELECT ##t.* FROM ##t; DROP TABLE ##t"),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="8","Global temp syntax, OBJECT_ID and star qualifier work in isolation");
  result=await sqlService.PreviewAsync(Query("CREATE TABLE #t([#n] int); INSERT #t VALUES(9); SELECT [#n] FROM #t; DROP TABLE #t"),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="9","Temp-like column names remain unchanged");
  await new SqlCommand($"CREATE TABLE [##{globalName}](n int); INSERT [##{globalName}] VALUES(123)",connection).ExecuteNonQueryAsync();
  result=await sqlService.PreviewAsync(Query($"IF OBJECT_ID('tempdb..##{globalName}') IS NOT NULL DROP TABLE [##{globalName}]; CREATE TABLE [##{globalName}](n int); INSERT [##{globalName}] VALUES(456); SELECT * FROM [##{globalName}]; DROP TABLE [##{globalName}]"),@"TEST\iris-preview",default);
  Check(result.Tables[0].Rows[0][0]=="456" && Convert.ToInt32(await Scalar($"SELECT n FROM [##{globalName}]"))==123,"Existing global table cannot be overwritten or dropped");
  var parallel=await Task.WhenAll(new[]{11,22}.Select(n=>sqlService.PreviewAsync(Query($"SELECT {n} AS n INTO ##same; SELECT * FROM ##same; DROP TABLE ##same"),@"TEST\iris-preview",default)));
  Check(parallel[0].Tables[0].Rows[0][0]=="11" && parallel[1].Tables[0].Rows[0][0]=="22","Concurrent global temp names cannot mix users' results");
  foreach(var request in new[]{Query($"DELETE FROM dbo.[{tableName}]"),Query("SELECT 1") with {SourceType="STORED_PROCEDURE",Procedure="sys.sp_executesql"}}) {
   try {await sqlService.PreviewAsync(request,@"TEST\iris-preview",default);throw new Exception("Unsafe request accepted");}
   catch(InvalidOperationException ex){Check(ex.Message.StartsWith("Iris.Preview."),"Unsafe request blocked with audit");}
  }
  await File.WriteAllTextAsync(file,$"UPDATE dbo.[{tableName}] SET Id=9");
  try {await sqlService.PreviewAsync(Query("") with {SourceType="SQL_FILE",FilePath=file},@"TEST\iris-preview",default);throw new Exception("Unsafe file accepted");}
  catch(InvalidOperationException ex){Check(ex.Message=="Iris.Preview.SelectOnly","Modifying SQL file blocked");}
  Check(Convert.ToInt32(await Scalar($"SELECT Id FROM dbo.[{tableName}]"))==1,"Rejected SQL did not change database row");
  Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM dbo.[{logName}] WHERE Status=N'REJECTED' AND CompletedAt IS NOT NULL"))==3,"Rejected attempts retained with completion time");
  try {await sqlService.PreviewAsync(Query("SELECT MissingColumn FROM sys.objects"),@"TEST\iris-preview",default);throw new Exception("SQL error not surfaced");}
  catch(SqlException){Check(true,"Runtime SQL error surfaced");}
  Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM dbo.[{logName}] WHERE Status=N'FAILED' AND ErrorMessage LIKE N'SQL 207:%'"))==1,"SQL errors retained in audit");
 } finally {
  // Names are generated locally, not accepted from request data.
  foreach(var name in new[]{logName,procName,tableName,globalName})
   if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^IRIS_PREVIEW_TEST_(LOG|PROC|DATA|GLOBAL)_[a-f0-9]{32}$")) throw new Exception("Unsafe fixture cleanup target");
  await new SqlCommand($"IF OBJECT_ID(N'tempdb..##{globalName}',N'U') IS NOT NULL DROP TABLE [##{globalName}]",connection).ExecuteNonQueryAsync();
  await new SqlCommand($"IF OBJECT_ID(N'dbo.{procName}',N'P') IS NOT NULL DROP PROCEDURE dbo.[{procName}]; IF OBJECT_ID(N'dbo.{tableName}',N'U') IS NOT NULL DROP TABLE dbo.[{tableName}]; IF OBJECT_ID(N'dbo.{logName}',N'U') IS NOT NULL DROP TABLE dbo.[{logName}];",connection).ExecuteNonQueryAsync();
  var resolved=Path.GetFullPath(testDirectory);var expected=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"iris-preview-sql-"+suffix));
  if(resolved!=expected)throw new Exception("Unsafe fixture directory cleanup target");
  Directory.Delete(resolved,true);
 }
}
Console.WriteLine($"ALL {passed} CHECKS PASSED. SQL integration fixtures removed when enabled; no emails sent.");

sealed class FakeAudit:IIrisPreviewAuditStore {
 public string? Actor,Status,Sql;public bool FailStart;
 public Task<Guid> BeginAsync(IrisSourcePreviewRequest request,string actor,CancellationToken ct){if(FailStart)throw new InvalidOperationException("Iris.Preview.AuditUnavailable");Actor=actor;return Task.FromResult(Guid.NewGuid());}
 public Task PrepareAsync(Guid id,string sql,CancellationToken ct){Sql=sql;return Task.CompletedTask;}
 public Task ExecutionAsync(Guid id,string sql,CancellationToken ct)=>Task.CompletedTask;
 public Task CompleteAsync(Guid id,string status,int rows,long elapsed,string? database,string error){Status=status;return Task.CompletedTask;}
}
sealed class FakePreview:IIrisSourcePreviewService {
 public IrisSourcePreviewRequest? Request;public string? Actor;public bool Fail;
 public Task<IrisSourcePreviewResult> PreviewAsync(IrisSourcePreviewRequest request,string actor,CancellationToken ct){Request=request;Actor=actor;if(Fail)throw new InvalidOperationException("Iris.Preview.SelectOnly");return Task.FromResult(new IrisSourcePreviewResult(new[]{new IrisPreviewTable(new[]{new IrisPreviewColumn("n","int",false)},new IReadOnlyList<string>[] {new[]{"7"}},false)},false,500,Guid.NewGuid()));}
}
sealed class TestUser:IUserNameResolver {public string ResolveUserName(ClaimsPrincipal user)=>@"TEST\server-identity";}
sealed class TestEnvironment:IWebHostEnvironment {
 public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="test";
 public string ContentRootPath{get;set;}="";public string WebRootPath{get;set;}="";
 public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
