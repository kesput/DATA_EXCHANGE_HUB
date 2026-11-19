using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Bondski.QvdLib;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("lt-LT");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("lt-LT");
var fixture = Path.Combine(Path.GetTempPath(), "DATA_EXCHANGE_HUB_QvdReports_" + Guid.NewGuid().ToString("N"));
var data = Path.Combine(fixture, "data");
Directory.CreateDirectory(data);
var count = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
IConfiguration Config(params (string Key, string Value)[] overrides)
{
    var values = new Dictionary<string, string?>
    {
        ["QvdReports:Sources:TEST:RootPath"] = data,
        ["QvdReports:Sources:TEST:Countries:0"] = "LT",
        ["QvdReports:Sources:TEST:Modules:0"] = "Apis",
        ["QvdReports:Sources:TEST:Modules:1"] = "Arete",
        ["QvdReports:Sources:TEST:Modules:2"] = "Hydra",
        ["QvdReports:Sources:TEST:Modules:3"] = "Kairos"
    };
    foreach (var (key, value) in overrides) values[key] = value;
    return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
string WriteQuery(string name, string sql, string module = "Apis", string pattern = "{date:yyyy.MM.dd}_ops.qvd")
{
    var directory = Path.Combine(fixture, "Sql", module, "LT", "QVD", "Processes");
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, name + ".sql"), $"""
-- SQL_PARAM_MUST: @SNDATE|date||Start
-- SQL_PARAM_MUST: @ENDATE|date||End
-- SQL_PARAM_MUST: @OWNER|text||Owner
-- QVD_SOURCE: ops|TEST|{pattern}|@SNDATE|@ENDATE
-- QVD_COLUMN: ops|QTY|decimal
-- QVD_COLUMN: ops|DATE|date
{sql}
""");
    return $"Sql/{module}/LT/QVD/Processes/{name}.sql";
}
ApisParameterValue[] Params(string owner = "001", string from = "2026-11-16", string to = "2026-11-17") =>
    [new("@SNDATE", from), new("@ENDATE", to), new("@OWNER", owner)];
ApisQueryService Apis(IConfiguration? config = null) => new(config ?? Config(), new HostEnvironment(fixture), Options.Create(new ApisOptions()), new AllowedQvdReports());
async Task<ApisQueryResult> Execute(string sql, IConfiguration? config = null, string owner = "001", string from = "2026-11-16", string to = "2026-11-17", string pattern = "{date:yyyy.MM.dd}_ops.qvd")
{
    var id = WriteQuery("Test", sql, pattern: pattern);
    return await Apis(config).ExecuteAsync(id, Params(owner, from, to), new HashSet<string>(["LT"]));
}
async Task Reject(string sql, string message, IConfiguration? config = null, string pattern = "{date:yyyy.MM.dd}_ops.qvd")
{
    try { await Execute(sql, config, pattern: pattern); }
    catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException)
    { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
try
{
    WriteQvd(Path.Combine(data, "2026.11.16_ops.qvd"), ["OWNER", "ITEM", "QTY", "CODE", "DATE"],
        [["001", "A", "2.5", "000001", "2026-11-16"], ["002", "B", "3", "000002", "2026-11-16"]]);
    WriteQvd(Path.Combine(data, "2026.11.17_ops.qvd"), ["OWNER", "ITEM", "QTY", "CODE", "DATE"],
        [["001", "A", "-1.25", "000003", "2026-11-17"], ["001", "B", "4", "000004", "2026-11-17"]]);
    WriteQvd(Path.Combine(data, "catalog.qvd"), ["ITEM", "NAME"], [["A", "Alpha"], ["B", "Beta"]]);
    using (var reader = new QvdReader(Path.Combine(data, "2026.11.16_ops.qvd")))
        Check(reader.NextRow() && reader["CODE"].ToString() == "000001", "Real QVD binary fixture round-trip");
    var basic = "SELECT OWNER,ITEM,QTY,CODE,[DATE],_QVD_FILE,_QVD_DATE FROM ops WHERE OWNER=@OWNER ORDER BY CODE;";
    var result = await Execute(basic);
    Check(result.Rows.Count == 3 && result.Rows[0]["CODE"] == "000001", "Multiple daily files, filter, stable ordering and leading zero codes");
    Check(result.ReadStatistics is { ScannedRows: 4, RetainedRows: 3 },
        "Source filters discard rows before retaining them in the execution table");
    Check(result.NumericColumns?.Contains("QTY") == true && !result.NumericColumns.Contains("CODE"), "Numeric metadata retained for CSV / Excel");
    Check(result.Rows.Select(row => row["_QVD_FILE"]).Distinct().Count() == 2, "Relative source filename is available per row");
    var sharedRootConfig = Config(("QvdReports:Sources:TEST:RootPath", ""),
        ("QvdReports:Sources:TEST:PreviewRootIndex", "1"), ("Apis:QvdRoots:0", data));
    var preview = new ApisQvdPreviewService(new HostEnvironment(fixture),
        Options.Create(sharedRootConfig.GetSection("Apis").Get<ApisOptions>()!));
    Check(preview.ListRoots()[1].Path == data && preview.ListFiles().Count == 3,
        "Shared source is the existing ARGUS QVD preview root");
    var sharedResult = await Execute(basic, sharedRootConfig);
    Check(sharedResult.Rows.Count == 3 && sharedResult.Rows[0]["CODE"] == "000001",
        "Reports read the ARGUS root without a duplicate RootPath");
    var otherData = Path.Combine(fixture, "other-data");
    Directory.CreateDirectory(otherData);
    foreach (var day in new[] { "16", "17" })
        WriteQvd(Path.Combine(otherData, $"2026.11.{day}_ops.qvd"), ["OWNER", "ITEM", "QTY", "CODE", "DATE"],
            [["001", "X", "10", "000009", $"2026-11-{day}"]]);
    sharedRootConfig["Apis:QvdRoots:0"] = otherData;
    sharedResult = await Execute(basic, sharedRootConfig);
    Check(sharedResult.Rows.Count == 2 && sharedResult.Rows.All(row => row["ITEM"] == "X"),
        "Changing the ARGUS root also changes the report source");
    var builtIn = Path.Combine(fixture, "Data", "Apis", "Qvd");
    Directory.CreateDirectory(builtIn);
    foreach (var day in new[] { "16", "17" })
        File.Copy(Path.Combine(data, $"2026.11.{day}_ops.qvd"), Path.Combine(builtIn, $"2026.11.{day}_ops.qvd"));
    sharedRootConfig["QvdReports:Sources:TEST:PreviewRootIndex"] = "0";
    Check((await Execute(basic, sharedRootConfig)).Rows.Count == 3,
        "Preview root 0 uses the ARGUS built-in Data/Apis/Qvd folder");
    await Reject(basic, "A missing ARGUS preview root is rejected", Config(
        ("QvdReports:Sources:TEST:RootPath", ""), ("QvdReports:Sources:TEST:PreviewRootIndex", "2"), ("Apis:QvdRoots:0", data)));
    await Reject(basic, "A negative ARGUS preview root index is rejected", Config(
        ("QvdReports:Sources:TEST:RootPath", ""), ("QvdReports:Sources:TEST:PreviewRootIndex", "-1")));
    await Reject(basic, "Conflicting direct and ARGUS root settings are rejected", Config(
        ("QvdReports:Sources:TEST:PreviewRootIndex", "0")));
    await Reject(basic, "A shared ARGUS root retains report country restrictions", Config(
        ("QvdReports:Sources:TEST:RootPath", ""), ("QvdReports:Sources:TEST:PreviewRootIndex", "1"),
        ("Apis:QvdRoots:0", data), ("QvdReports:Sources:TEST:Countries:0", "EE")));
    Check(Apis().ListQueries(new HashSet<string>(["EE"])).Count == 0, "QVD reports retain existing country visibility restrictions");
    var workbookService = new ExcelWorkbookService();
    var utf8 = workbookService.CreateSemicolonCsv(result.Columns, result.Rows, false, result.NumericColumns, result.ValueCulture);
    Check(utf8.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "QVD result exports as UTF-8 CSV with BOM");
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var csv = Encoding.GetEncoding(1257).GetString(workbookService.CreateSemicolonCsv(result.Columns, result.Rows, true, result.NumericColumns, result.ValueCulture));
    Check(csv.Contains("\"-1.25\"") && csv.Contains("\"000003\""), "QVD Windows-1257 CSV preserves decimal points and text codes");
    using (var workbook = new MemoryStream(workbookService.CreateWorkbook(result.Columns, result.Rows, "QVD", gridlines: true)))
    {
        var sheet = workbookService.ReadWorkbook(workbook);
        Check(sheet.Rows.Count == 3 && sheet.Rows[0]["CODE"] == "000001", "QVD Excel export with borders round-trips without losing leading zeros");
    }
    result = await Execute("SELECT ITEM,SUM(QTY) AS QTY,COUNT(*) AS N FROM ops WHERE OWNER=@OWNER GROUP BY ITEM ORDER BY ITEM;");
    Check(result.Rows.Count == 2 && decimal.Parse(result.Rows[0]["QTY"]!, CultureInfo.CurrentCulture) == 1.25m && result.Rows[1]["N"] == "1", "SUM / COUNT / GROUP BY use typed QVD values");
    result = await Execute("-- QVD_SOURCE: products|TEST|catalog.qvd\nSELECT o.ITEM,p.NAME,SUM(o.QTY) AS QTY FROM ops o LEFT JOIN products p ON p.ITEM=o.ITEM WHERE o.OWNER=@OWNER GROUP BY o.ITEM,p.NAME ORDER BY o.ITEM;");
    Check(result.Rows.Count == 2 && result.Rows[0]["NAME"] == "Alpha", "JOIN between daily and static QVD sources");
    var optionalJoin = WriteQuery("OptionalJoin", """
-- QVD_SOURCE: products|TEST|catalog.qvd
-- SQL_PARAM: @PRODUCT_NAME|text||Name|operators==,like,in;expr=p.NAME
-- SQL_PARAM: @CODE|text||Code|operators==,like,in;expr=o.CODE
SELECT o.CODE FROM ops o
LEFT JOIN products p ON p.ITEM=o.ITEM
WHERE o.OWNER=@OWNER
/* SQL_PARAM_FILTERS */
ORDER BY o.CODE;
""");
    Task<ApisQueryResult> OptionalJoin(params ApisParameterValue[] filters) => Apis().ExecuteAsync(optionalJoin,
        [.. Params(), .. filters], new HashSet<string>(["LT"]));
    Check((await OptionalJoin()).Rows.Count == 3, "No selected optional JOIN filters retain the mandatory result");
    Check((await OptionalJoin(new ApisParameterValue("@PRODUCT_NAME", "Alpha"))).Rows.Count == 2,
        "Optional filters can refer to a joined table's column absent from SELECT");
    Check((await OptionalJoin(new ApisParameterValue("@PRODUCT_NAME", "ph", "like"))).Rows.Count == 2 &&
        (await OptionalJoin(new ApisParameterValue("@PRODUCT_NAME", "Al_ha", "like"))).Rows.Count == 2,
        "Joined-table LIKE supports fragments and underscore wildcards in QVD");
    Check((await OptionalJoin(new ApisParameterValue("@PRODUCT_NAME", "Alpha, Beta", "in"))).Rows.Count == 3,
        "Joined-table IN splits and trims selected values");
    Check((await OptionalJoin(new("@PRODUCT_NAME", "Alpha"), new("@CODE", "000002", Connector: "or"))).Rows.Count == 2,
        "Optional JOIN OR cannot include another mandatory owner's records");
    Check((await OptionalJoin(new ApisParameterValue("@PRODUCT_NAME", "Alpha' OR 1=1 --"))).Rows.Count == 0,
        "Optional JOIN values remain bound parameters");
    result = await Execute("SELECT COUNT(*) AS N FROM ops;");
    Check(result.Rows.Single()["N"] == "4", "COUNT(*) works when no physical QVD columns are projected");
    result = await Execute("SELECT _QVD_DATE,COUNT(*) AS N FROM ops GROUP BY _QVD_DATE ORDER BY _QVD_DATE;");
    Check(result.Rows.Count == 2 && result.Rows.All(row => row["N"] == "2"), "Synthetic date columns work without physical column projection");
    result = await Execute("SELECT OWNER,COUNT(*) AS N FROM ops WHERE QTY < 0 GROUP BY OWNER;");
    Check(result.Rows.Single()["N"] == "1", "Projection retains columns referenced only by a filter");
    result = await Execute("SELECT * FROM ops ORDER BY CODE;");
    Check(result.Columns.Count == 7 && result.Rows.Count == 4 && result.Rows[0]["CODE"] == "000001", "SELECT * retains every QVD and synthetic column");
    result = await Execute("SELECT x.* FROM (SELECT CODE,ITEM FROM ops WHERE OWNER=@OWNER) x ORDER BY CODE;");
    Check(result.Columns.Count == 2 && result.Rows.Count == 3, "Nested qualified stars retain all columns needed by their subquery");
    result = await Execute("WITH a AS (SELECT ITEM,QTY FROM ops WHERE OWNER=@OWNER) SELECT TOP (1) ITEM,SUM(QTY) AS QTY FROM a GROUP BY ITEM ORDER BY QTY DESC;");
    Check(result.Rows.Count == 1 && result.Rows[0]["ITEM"] == "B", "CTE and outer TOP respect final ordering");
    result = await Execute("SELECT ITEM,LEN(ITEM) AS N FROM ops WHERE OWNER IN (SELECT value FROM STRING_SPLIT(@OWNER, ','));");
    Check(result.Rows.Count == 3 && result.Rows.All(row => row["N"] == "1"), "STRING_SPLIT and translated scalar function");
    Check(result.ReadStatistics?.RetainedRows == 3, "IN / STRING_SPLIT is applied to bounded source batches");
    result = await Execute("SELECT CODE FROM ops WHERE OWNER=@OWNER AND (CODE='000001' OR CODE='000004') ORDER BY CODE;",
        Config(("QvdReports:MaxInputRows", "2")));
    Check(result.Rows.Count == 2 && result.ReadStatistics?.RetainedRows == 2,
        "A complete OR group is filtered before the retained-row budget check");
    result = await Execute("SELECT CODE FROM ops WHERE LEN(CODE)=6 AND QTY<0;", Config(("QvdReports:MaxInputRows", "1")));
    Check(result.Rows.Single()["CODE"] == "000003" && result.ReadStatistics?.RetainedRows == 1,
        "Native numeric comparisons and scalar functions filter source batches");
    result = await Execute("SELECT CODE FROM ops WHERE ITEM COLLATE NOCASE='a';");
    Check(result.Rows.Count == 2 && result.ReadStatistics?.RetainedRows == 2,
        "Explicit collation comparisons keep native SQL semantics during filtering");
    result = await Execute("SELECT a.CODE,b.CODE AS OTHER FROM ops a LEFT JOIN ops b ON b.ITEM=a.ITEM WHERE a.CODE='000001' ORDER BY b.CODE;");
    Check(result.Rows.Count == 2 && result.Rows[1]["OTHER"] == "000003" && result.ReadStatistics?.RetainedRows == 4,
        "A self join retains the source rows needed by its other use");
    result = await Execute("WITH all_rows AS (SELECT CODE,ITEM FROM ops) SELECT a.CODE,b.CODE AS OTHER FROM ops a LEFT JOIN all_rows b ON b.ITEM=a.ITEM WHERE a.CODE='000001' ORDER BY b.CODE;");
    Check(result.Rows.Count == 2 && result.ReadStatistics?.RetainedRows == 4,
        "A source shared with a CTE is not incorrectly reduced to the outer filter");
    foreach (var joinType in new[] { "RIGHT", "FULL" })
    {
        result = await Execute("-- QVD_SOURCE: products|TEST|catalog.qvd\nSELECT o.CODE,p.ITEM FROM ops o " + joinType +
            " JOIN products p ON p.ITEM=o.ITEM WHERE o.CODE='000001' OR o.CODE IS NULL;");
        Check(result.Rows.Count == 1 && result.ReadStatistics?.RetainedRows == 6,
            joinType + " JOIN safely falls back to retaining both inputs");
    }
    result = await Execute(basic, owner: "x' OR 1=1 --");
    Check(result.Rows.Count == 0, "Values are bound parameters, not interpolated SQL");
    result = await Execute("SELECT TOP 0 ITEM FROM ops;");
    Check(result.Rows.Count == 0 && result.Columns.Single() == "ITEM", "Empty result retains its columns");
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
    result = await Execute(basic, from: "16/11/2026", to: "17/11/2026");
    Check(result.Rows.Count == 3 && result.ValueCulture == "en-GB" && decimal.Parse(result.Rows[1]["QTY"]!, CultureInfo.CurrentCulture) == -1.25m, "EN date input and numeric formatting");
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("lt-LT");

    foreach (var module in new[] { "Arete", "Hydra", "Kairos" })
    {
        var id = WriteQuery("Daily", basic, module);
        ExchangeTableFilter[] filters = Params().Select(parameter => new ExchangeTableFilter(parameter.Name, "eq", parameter.Value!, "and")).ToArray();
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows = module switch
        {
            "Arete" => (await new AreteQueryService(Config(), new HostEnvironment(fixture), new AllowedQvdReports()).ExecuteAsync(id, filters, new HashSet<string>(["LT"]))).Rows,
            "Hydra" => (await new HydraQueryService(Config(), new HostEnvironment(fixture), new AllowedQvdReports()).ExecuteAsync(id, filters, new HashSet<string>(["LT"]))).Rows,
            _ => (await new KairosAnalysisQueryService(Config(), new HostEnvironment(fixture), new AllowedQvdReports()).ExecuteAsync(id, filters, new HashSet<string>(["LT"]))).Rows
        };
        Check(rows.Count == 3 && rows[0]["CODE"] == "000001", module + " discovers and executes QVD query without SQL Server");
    }
    foreach (var sql in new[]
    {
        "DELETE FROM ops;", "UPDATE ops SET QTY=0;", "INSERT INTO ops SELECT * FROM ops;", "DROP TABLE ops;",
        "ALTER TABLE ops ADD x INT;", "CREATE TABLE x(i INT);", "SELECT * INTO x FROM ops;",
        "SELECT * FROM ops; DELETE FROM ops;", "SELECT * FROM OPENROWSET(BULK 'x',SINGLE_CLOB) x;",
        "SELECT * FROM read_csv('x');", "SELECT * FROM information_schema.tables;", "SELECT * FROM secret;",
        "SELECT getenv('PATH');", "SELECT * FROM ops WITH (NOLOCK);", "SELECT TOP 1 PERCENT * FROM ops;"
    }) await Reject(sql, "Read-only SELECT guard: " + sql);
    await Reject(basic, "Source denied for another country", Config(("QvdReports:Sources:TEST:Countries:0", "EE")));
    await Reject(basic, "Source denied for another module", Config(("QvdReports:Sources:TEST:Modules:0", "Other")));
    await Reject(basic, "Directory traversal rejected", pattern: "../{date:yyyy.MM.dd}_ops.qvd");
    await Reject(basic, "Absolute path rejected", pattern: "C:/{date:yyyy.MM.dd}_ops.qvd");
    await Reject(basic, "Missing daily file rejected", pattern: "{date:yyyy.MM.dd}_missing.qvd");
    await Reject(basic, "A daily pattern must contain a day component", pattern: "{date:yyyy.MM}_ops.qvd");
    await Reject(basic, "Date range limit", Config(("QvdReports:MaxDays", "1")));
    await Reject(basic, "File count limit", Config(("QvdReports:MaxFiles", "1")));
    await Reject(basic, "File size limit", Config(("QvdReports:MaxFileBytes", "1")));
    await Reject(basic, "Total size limit", Config(("QvdReports:MaxTotalBytes", "1")));
    await Reject(basic, "Retained input row limit", Config(("QvdReports:MaxInputRows", "2")));
    Check((await Execute(basic, Config(("QvdReports:MaxInputRows", "3")))).Rows.Count == 3,
        "Rejected source rows do not consume the retained row budget");
    await Reject(basic, "Scanned row limit", Config(("QvdReports:MaxScannedRows", "3")));
    await Reject(basic, "Scanned byte limit", Config(("QvdReports:MaxScanBytes", "1")));
    await Reject("-- QVD_SOURCE: products|TEST|catalog.qvd\n-- QVD_MATCH: ops|ITEM|products|ITEM\nSELECT ITEM FROM ops;",
        "QVD_MATCH requires its parent source to be declared first");
    await Reject("-- QVD_SOURCE: products|TEST|catalog.qvd\n-- QVD_MATCH: products|ITEM|ops|MISSING_COLUMN\nSELECT ITEM FROM ops;",
        "QVD_MATCH rejects unknown key columns");
    await Reject("-- QVD_SOURCE: products|TEST|catalog.qvd\n-- QVD_MATCH: products|ITEM|ops|ITEM,CODE\nSELECT ITEM FROM ops;",
        "QVD_MATCH rejects mismatched key lengths");
    await Reject(basic, "Result row limit fails instead of returning partial data", Config(("QvdReports:MaxResultRows", "2")));
    await Reject("SELECT * FROM ops", "Column limit", Config(("QvdReports:MaxColumns", "2")));
    WriteQvd(Path.Combine(data, "2026.11.18_ops.qvd"), ["OWNER", "ITEM", "QTY", "CODE", "DATE"], []);
    result = await Execute(basic, from: "2026-11-18", to: "2026-11-18");
    Check(result.Rows.Count == 0 && result.Columns.Count == 7, "An existing zero-record day is a valid empty result");
    var parallelId = WriteQuery("Parallel", basic);
    var parallel = await Task.WhenAll(new[] { "001", "002" }.Select(owner =>
        Apis().ExecuteAsync(parallelId, Params(owner), new HashSet<string>(["LT"]))));
    Check(parallel[0].Rows.Count == 3 && parallel[1].Rows.Count == 1, "Concurrent requests have isolated in-memory data and parameters");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    await Reject("SELECT SUM(a.QTY) AS N FROM ops a " + string.Join(" ", Enumerable.Range(0, 18).Select(n => "CROSS JOIN ops j" + n)),
        "Expensive SQL is interrupted by the query deadline", Config(("QvdReports:TimeoutSeconds", "1")));
    Check(watch.Elapsed < TimeSpan.FromSeconds(10), "Query cancellation does not wait for the Cartesian product to finish");
    WriteQvd(Path.Combine(data, "2026.11.17_ops.qvd"), ["OWNER", "OTHER"], [["001", "A"]]);
    await Reject(basic, "Schema drift fails instead of merging wrong columns");
    await FlowsChecks.RunAsync(Directory.GetCurrentDirectory(), fixture, WriteQvd, Check);
    await QvdAccessChecks.RunAsync(fixture, Check);
    if (args.Contains("--filtered-flows"))
    {
        var root = Directory.GetCurrentDirectory();
        var actualConfig = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true).AddJsonFile("appsettings.Local.json", optional: true).Build();
        await FlowsChecks.RunFilteredRealAsync(root, actualConfig, Check);
    }
    if (args.Contains("--real") || args.Contains("--flows"))
    {
        var root = Directory.GetCurrentDirectory();
        var actualConfig = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true).AddJsonFile("appsettings.Local.json", optional: true).Build();
        var actual = new ApisQueryService(actualConfig, new HostEnvironment(root), Options.Create(new ApisOptions()), new AllowedQvdReports());
        var actualResult = await actual.ExecuteAsync("Sql/Apis/LT/QVD/Processes/LT_EMP_TTS_QVD_SUMMARY.sql",
            [new("@SNDATE", "2026-11-17"), new("@ENDATE", "2026-11-17")], new HashSet<string>(["LT"]));
        var actualPreview = new ApisQvdPreviewService(new HostEnvironment(root),
            Options.Create(actualConfig.GetSection("Apis").Get<ApisOptions>()!));
        var actualRootIndex = actualConfig.GetValue<int>("QvdReports:Sources:LT_EMP_TTS:PreviewRootIndex");
        using var actualQvd = new QvdReader(Path.Combine(actualPreview.ListRoots()[actualRootIndex].Path, "2026.11.17_EMP_TTS.qvd"));
        Check(actualResult.Rows.Count > 0 && actualResult.Rows.Sum(row => long.Parse(row["EVENTS"]!)) == actualQvd.Header.NoOfRecords,
            "Real EMP_TTS summary matches the complete daily QVD record count");
        Check(actualResult.NumericColumns?.Contains("EVENTS") == true && actualResult.NumericColumns.Contains("WORKERS"),
            "Real QVD report retains numeric export metadata");
        if (args.Contains("--flows")) await FlowsChecks.RunRealAsync(root, actualConfig, Check);
    }
    Console.WriteLine($"ALL {count} QVD REPORT CHECKS PASSED.");
}
finally
{
    var full = Path.GetFullPath(fixture);
    if (Path.GetDirectoryName(full) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
        Path.GetFileName(full).StartsWith("DATA_EXCHANGE_HUB_QvdReports_", StringComparison.Ordinal)) Directory.Delete(full, true);
}

static void WriteQvd(string path, string[] fields, string[][] rows)
{
    var symbols = fields.Select((_, column) => rows.Select(row => row[column]).Distinct(StringComparer.Ordinal).ToArray()).ToArray();
    var widths = symbols.Select(values => Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(1, values.Length))))).ToArray();
    var headers = new List<XElement>();
    using var binary = new MemoryStream();
    var bitOffset = 0;
    for (var column = 0; column < fields.Length; column++)
    {
        var offset = binary.Length;
        foreach (var symbol in symbols[column])
        {
            if (fields[column] == "CODE") { binary.WriteByte(5); binary.Write(BitConverter.GetBytes(int.Parse(symbol, CultureInfo.InvariantCulture))); }
            else binary.WriteByte(4);
            binary.Write(Encoding.UTF8.GetBytes(symbol)); binary.WriteByte(0);
        }
        headers.Add(new("QvdFieldHeader", new XElement("FieldName", fields[column]), new XElement("BitOffset", bitOffset),
            new XElement("BitWidth", widths[column]), new XElement("Bias", 0), new XElement("NoOfSymbols", symbols[column].Length),
            new XElement("Offset", offset), new XElement("Length", binary.Length - offset),
            new XElement("NumberFormat", new XElement("Type", "ASCII"))));
        bitOffset += widths[column];
    }
    var recordOffset = binary.Length;
    var recordSize = (bitOffset + 7) / 8;
    foreach (var row in rows)
    {
        var record = new byte[recordSize];
        var bit = 0;
        for (var column = 0; column < fields.Length; column++)
        {
            var index = Array.IndexOf(symbols[column], row[column]);
            for (var b = 0; b < widths[column]; b++, bit++) if ((index & (1 << b)) != 0) record[bit / 8] |= (byte)(1 << (bit % 8));
        }
        binary.Write(record);
    }
    var xml = new XElement("QvdTableHeader", new XElement("QvBuildNo", "1"), new XElement("TableName", "Test"),
        new XElement("Fields", headers), new XElement("Compression", ""), new XElement("RecordByteSize", recordSize),
        new XElement("NoOfRecords", rows.Length), new XElement("Offset", recordOffset), new XElement("Length", rows.Length * recordSize));
    using var file = File.Create(path);
    file.Write(Encoding.UTF8.GetBytes(xml.ToString() + "\r\n\0"));
    binary.Position = 0; binary.CopyTo(file);
}

sealed class HostEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "DATA_EXCHANGE_HUB";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = root;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class AllowedQvdReports : IQvdReportAccessService
{
    public Task<bool> CanReadAsync(string module, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
