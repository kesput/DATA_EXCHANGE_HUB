using System.Globalization;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("lt-LT");
var count=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
var model=new ExchangeTableData("test",[],[],[],"REPORTNAME, POSID",10,1,false,["REPORTNAME","POSID"]);
Check(model.CanUpdateRows && model.IsKeyColumn("reportname") && model.IsKeyColumn("posid") && !model.IsKeyColumn("ISACTIVE"),"Composite key columns are protected while non-key values can be edited");
Check(new ExchangeTableData("test",[],[],[],"ID",10,1,false).IsKeyColumn("ID"),"Legacy single-key model remains compatible");
if(!args.Contains("--sql")){Console.WriteLine($"ALL {count} OFFLINE CHECKS PASSED. Pass --sql for isolated SQL fixtures.");return;}
var config=new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Local.json",optional:true).Build();
var prefix="DX_TEST_"+Guid.NewGuid().ToString("N");
string Name(string suffix)=>"dbo."+prefix+"_"+suffix;
var created=new List<string>();
await using var connection=new SqlConnection(config.GetConnectionString("DataExchangeSql"));await connection.OpenAsync();
async Task Exec(string sql){await using var command=new SqlCommand(sql,connection);await command.ExecuteNonQueryAsync();}
async Task Create(string suffix,string schema){await Exec($"CREATE TABLE {Name(suffix)} ({schema});");created.Add(Name(suffix));}
async Task<object?> Scalar(string sql){await using var command=new SqlCommand(sql,connection);return await command.ExecuteScalarAsync();}
var actor="DX_TEST_"+Guid.NewGuid().ToString("N");
try {
 await Create("Log","LOG_ID BIGINT IDENTITY PRIMARY KEY, EVENT_AT DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(), BATCH_ID UNIQUEIDENTIFIER NOT NULL, ACTION_TYPE NVARCHAR(30) NOT NULL, SOURCE NVARCHAR(50) NOT NULL, TABLE_NAME NVARCHAR(776) NOT NULL, KEY_COLUMN NVARCHAR(128) NULL, KEY_VALUE NVARCHAR(4000) NULL, ACTOR_USER NVARCHAR(256) NOT NULL, CHANGED_COLUMNS NVARCHAR(MAX) NULL, ROW_DATA_BEFORE NVARCHAR(MAX) NULL, ROW_DATA_AFTER NVARCHAR(MAX) NULL, IS_REVERTED BIT NOT NULL DEFAULT 0, REVERTED_AT DATETIMEOFFSET NULL, REVERTED_BY NVARCHAR(256) NULL, REVERT_SOURCE_LOG_ID BIGINT NULL");
 await Create("Composite","REPORTNAME VARCHAR(128) NOT NULL, POSID INT NOT NULL, UPDATE_TIME DATETIME2 NOT NULL DEFAULT SYSDATETIME(), ISACTIVE BIT NOT NULL DEFAULT 1, PRIMARY KEY(REPORTNAME,POSID)");
 await Create("Single","ID INT IDENTITY PRIMARY KEY, NAME NVARCHAR(100) NOT NULL, ISACTIVE BIT NOT NULL DEFAULT 1");
 await Create("Filtered","A INT NOT NULL, B INT NOT NULL, ISACTIVE BIT NOT NULL");
 await Exec($"CREATE UNIQUE INDEX UX_FILTERED ON {Name("Filtered")}(A) WHERE ISACTIVE=1; CREATE UNIQUE INDEX UX_FULL ON {Name("Filtered")}(A,B);");
 await Create("NoKey","NAME NVARCHAR(50), ISACTIVE BIT NOT NULL");
 var service=new DataExchangeTableService(config,Options.Create(new DataExchangeOptions{ConnectionStringName="DataExchangeSql",ExchangeLogTableName=Name("Log")}));
 ExchangeTableAccess Access(string suffix)=>new(Name(suffix),"ADMIN",ExchangePermission.UploadAndReceive);
 var table=Access("Composite");
 await Exec($"INSERT INTO {Name("Composite")} (REPORTNAME,POSID,ISACTIVE,UPDATE_TIME) VALUES ('A',10,1,'20000101'),('A',11,1,'20000101'),('B',10,1,'20000101'),('001.200',10,1,'20000101'),(' spaced ',10,1,'20000101');");
 async Task<ExchangeTableData> Load(ExchangeTableAccess? access=null)=>await service.GetTableDataAsync(access??table,100,1);
 async Task<ExchangeTableRow> Row(string report,int pos=10)=>(await Load()).Rows.Single(row=>row.Values["REPORTNAME"]==report && row.Values["POSID"]==pos.ToString(CultureInfo.InvariantCulture));
 async Task<bool> Active(string report,int pos=10)=>bool.Parse((await Row(report,pos)).Values["ISACTIVE"]);
 Task Update(string key,string value)=>service.UpdateRowsAsync(table,new Dictionary<string,Dictionary<string,string?>>{[key]=new(){["ISACTIVE"]=value}},actor);
 async Task<long> LastLog(string action)=>Convert.ToInt64(await Scalar($"SELECT MAX(LOG_ID) FROM {Name("Log")} WHERE ACTION_TYPE='{action}';"));
 async Task Reject(Func<Task> action,string message){var rejected=false;try{await action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
 var data=await Load();Check(data.CanUpdateRows && data.KeyColumns.SequenceEqual(new[]{"REPORTNAME","POSID"}),"SQL composite primary key is recognized without ID");
 var aKey=(await Row("A")).KeyValue!;var bKey=(await Row("B")).KeyValue!;
 Check(aKey!=bKey && JsonDocument.Parse(aKey).RootElement.GetProperty("POSID").GetString()=="10","Same POSID in different reports has a distinct stable key");
 await Update(aKey,"false");Check(!await Active("A") && await Active("B") && await Active("A",11),"true to false changes exactly REPORTNAME plus POSID");
 Check(DateTime.Parse((await Row("A")).Values["UPDATE_TIME"]).Year>2000,"Automatic UPDATE_TIME is retained");
 var firstLog=await LastLog("UPDATE");var logs=await service.ListExchangeLogsAsync(table,actor,false);
 Check(logs[0].ActorUser==actor && logs[0].KeyValue==aKey && logs[0].KeyColumn=="REPORTNAME, POSID","Audit records actor and complete composite key");
 Check(JsonDocument.Parse((string)(await Scalar($"SELECT ROW_DATA_BEFORE FROM {Name("Log")} WHERE LOG_ID={firstLog};"))!).RootElement.GetProperty("ISACTIVE").GetString()=="True" && JsonDocument.Parse((string)(await Scalar($"SELECT ROW_DATA_AFTER FROM {Name("Log")} WHERE LOG_ID={firstLog};"))!).RootElement.GetProperty("ISACTIVE").GetString()=="False","Audit records before and after boolean values");
 await Update(aKey,"true");Check(await Active("A"),"false to true is supported");var secondLog=await LastLog("UPDATE");
 await Reject(()=>service.RevertLogEntryAsync(table,secondLog,"another_actor",false),"Other user's audit cannot be reverted without administrator rights");
 await service.RevertLogEntryAsync(table,secondLog,actor,false);Check(!await Active("A") && await Active("B"),"Audit revert uses the entire composite key");
 await Reject(()=>service.RevertLogEntryAsync(table,secondLog,actor,false),"Same audit cannot be reverted twice");
 await service.RevertLogEntryAsync(table,firstLog,actor,false);Check(await Active("A"),"Initial active state can be restored through audit");
 var logCount=Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM {Name("Log")};"));
 foreach(var key in new[]{"10","{}","[]","{\"REPORTNAME\":\"A\"}","{\"REPORTNAME\":\"A\",\"POSID\":null}","{\"REPORTNAME\":\"A\",\"POSID\":\"10\",\"extra\":\"x\"}","{\"REPORTNAME\":\"A\",\"POSID\":\"10\",\"posid\":\"10\"}","{\"REPORTNAME\":\"A\",\"POSID\":\"10 OR 1=1\"}"}) await Reject(()=>Update(key,"false"),"Malformed/incomplete composite key rejected: "+key);
 await Reject(()=>Update(aKey,"not-a-boolean"),"Invalid bit value is rejected");
 var injection=JsonSerializer.Serialize(new Dictionary<string,string>{{"REPORTNAME","A' OR 1=1--"},{"POSID","10"}});await Reject(()=>Update(injection,"false"),"SQL-like report name is only a parameter value");
 Check(await Active("A") && await Active("B") && logCount==Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM {Name("Log")};")),"Rejected edits leave business data and audit unchanged");
 foreach(var report in new[]{"001.200"," spaced "}) {var key=(await Row(report)).KeyValue!;await Update(key,"false");Check(!await Active(report),"Text key is not normalized or trimmed: "+report);}
 await service.UpdateRowsAsync(table,new Dictionary<string,Dictionary<string,string?>>{[aKey]=new(){["REPORTNAME"]="CHANGED",["POSID"]="99",["ISACTIVE"]="false"}},actor);
 Check(!await Active("A") && !(await Load()).Rows.Any(row=>row.Values["REPORTNAME"]=="CHANGED"),"Composite key columns cannot be changed by a crafted update");
 await service.DeleteRowAsync(table,aKey,actor);Check(!(await Load()).Rows.Any(row=>row.KeyValue==aKey) && await Active("B"),"Single deletion uses both key columns");
 var deletedLog=await LastLog("DELETE");await service.RevertLogEntryAsync(table,deletedLog,actor,false);Check(!await Active("A") && await Active("B"),"Deleted composite-key row restores without touching matching POSID in another report");
 var form=new FormCollection(new Dictionary<string,StringValues>{{"REPORTNAME","C"},{"POSID","10"},{"ISACTIVE","true"}});await service.AddRowAsync(table,form,actor);Check(await Active("C"),"Manual insert still supports composite keys");
 var insertedLog=await LastLog("INSERT");Check(((string?)await Scalar($"SELECT KEY_VALUE FROM {Name("Log")} WHERE LOG_ID={insertedLog};"))?.Contains("POSID")==true,"Manual insert audit contains composite key");
 var processed=await service.AddRowsAsync(table,new IReadOnlyDictionary<string,string?>[]{new Dictionary<string,string?>{{"REPORTNAME","A"},{"POSID","10"},{"ISACTIVE","true"}},new Dictionary<string,string?>{{"REPORTNAME","D"},{"POSID","12"},{"ISACTIVE","false"}}},actor);
 Check(processed==2 && await Active("A") && !await Active("D",12),"Import upsert retains composite matching for existing and new rows");
 var removed=await service.DeleteRowsByFilterAsync(table,[new ExchangeTableFilter("REPORTNAME","=","D","AND")],actor);Check(removed==1 && !(await Load()).Rows.Any(row=>row.Values["REPORTNAME"]=="D"),"Filtered deletion and audit support composite-key rows");
 await service.RevertLogEntryAsync(table,await LastLog("DELETE"),actor,false);Check(!await Active("D",12),"Filtered deletion can be restored through audit");
 await service.AddRowAsync(Access("Single"),new FormCollection(new Dictionary<string,StringValues>{{"NAME","Legacy"},{"ISACTIVE","true"}}),actor);
 var single=await Load(Access("Single"));Check(single.KeyColumn=="ID" && single.Rows[0].KeyValue=="1","Single ID row keeps legacy string key format");
 await service.UpdateRowsAsync(Access("Single"),new Dictionary<string,Dictionary<string,string?>>{["1"]=new(){["ISACTIVE"]="false"}},actor);Check(bool.Parse((await Load(Access("Single"))).Rows[0].Values["ISACTIVE"])==false,"Single-key boolean editing remains supported");
 await service.RevertLogEntryAsync(Access("Single"),await LastLog("UPDATE"),actor,false);Check(bool.Parse((await Load(Access("Single"))).Rows[0].Values["ISACTIVE"]),"Single-key update audit revert remains supported");
 await Exec($"INSERT INTO {Name("Filtered")} VALUES (1,1,0),(1,2,0);");var filtered=await Load(Access("Filtered"));Check(filtered.KeyColumns.SequenceEqual(new[]{"A","B"}),"Filtered unique index is not treated as a global row key");
 Check(!(await Load(Access("NoKey"))).CanUpdateRows,"Tables without a reliable key remain read-only");
 await Reject(()=>service.UpdateRowsAsync(table with{Permission=ExchangePermission.Receive},new Dictionary<string,Dictionary<string,string?>>{[aKey]=new(){["ISACTIVE"]="false"}},actor),"Read-only access never permits updates");
 var real=await service.GetTableDataAsync(new ExchangeTableAccess("LSP_ORDERS_BY_WEEKDAY_POS","ADMIN",ExchangePermission.UploadAndReceive),10,1);
 Check(real.CanUpdateRows && real.KeyColumns.SequenceEqual(new[]{"REPORTNAME","POSID"}),"Real LSP table is editable using metadata; no real rows changed");
 Console.WriteLine($"ALL {count} DATAEXCHANGE EDITING CHECKS PASSED; only GUID test tables were changed.");
}
finally {
 foreach(var name in created.AsEnumerable().Reverse()) await Exec($"DROP TABLE {name};");
 Console.WriteLine("Isolated test tables and their audit removed.");
}
