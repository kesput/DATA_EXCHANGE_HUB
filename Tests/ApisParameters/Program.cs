using System.Data;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException) { Check(true, message); return; }
    throw new Exception(message);
}

var root = Directory.GetCurrentDirectory();
var fixtureRoot = Path.Combine(Path.GetTempPath(), "DATA_EXCHANGE_HUB_ApisParameters_" + Guid.NewGuid().ToString("N"));
var fixtureQueries = Path.Combine(fixtureRoot, "Sql", "Apis", "EE", "Stat3", "Processes");
Directory.CreateDirectory(fixtureQueries);
var buildTextCommand = typeof(ApisQueryService).GetMethod("BuildTextCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
string Bind(SqlCommand command, string sql, ApisQueryDefinition query, params ApisParameterValue[] values)
{
    try { return (string)buildTextCommand.Invoke(null, [command, sql, query, values])!; }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
        ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        throw;
    }
}
ApisQueryService Service(string contentRoot, IConfiguration? config = null) =>
    new(config ?? new ConfigurationBuilder().Build(), new HostEnvironment(contentRoot), Options.Create(new ApisOptions()));

try
{
    var declarations = new Dictionary<string, string>
    {
        ["NoRules"] = "-- SQL_PARAM_MUST: @DATE|date||Date",
        ["EmptyRules"] = "-- SQL_PARAM_MUST: @DATE|date||Date|",
        ["BlankRules"] = "-- SQL_PARAM_MUST: @DATE|date||Date|   ",
        ["Optional"] = "-- SQL_PARAM: @DATE|date||Date",
        ["Format"] = "-- SQL_PARAM_MUST: @DATE|date||Date|format=yyyyMMdd",
        ["Restricted"] = "-- SQL_PARAM_MUST: @DATE|date||Date|operators=ne",
        ["Text"] = "-- SQL_PARAM_MUST: @OWNER|text||Owner",
        ["OptionalFilters"] = "-- SQL_PARAM_MUST: @OWNER|text||Owner\n-- SQL_PARAM: @CODE|text||Code|operators==,like,in;expr=ITEMCODE\n-- SQL_PARAM: @NAME|text||Name|operators==,like,in;expr=ITEMNAME"
    };
    foreach (var entry in declarations)
        File.WriteAllText(Path.Combine(fixtureQueries, entry.Key + ".sql"), entry.Value + "\nSELECT @DATE;\n");
    var definitions = Service(fixtureRoot).ListQueries().ToDictionary(query => query.Name);
    foreach (var name in new[] { "NoRules", "EmptyRules", "BlankRules", "Optional" })
    {
        var query = definitions[name];
        Check(query.Parameters.Single().AllowedOperators.SequenceEqual(["eq"]), name + " defaults to equality");
        using var command = new SqlCommand();
        Bind(command, "SELECT @DATE;", query, new ApisParameterValue("@DATE", "2026-10-23"));
        Check(command.Parameters["@DATE"].SqlDbType == SqlDbType.Date &&
              command.Parameters["@DATE"].Value is DateTime date && date == new DateTime(2026, 10, 23),
              name + " binds a native SQL date through the APIS command builder");
    }
    foreach (var op in new[] { "", " ", "eq", "=" })
    {
        using var command = new SqlCommand();
        Bind(command, "SELECT @DATE;", definitions["NoRules"], new ApisParameterValue("@DATE", "2026-10-23", op));
        Check((string)command.Parameters["@DATE_OPERATOR"].Value == "eq", "Equality accepted: '" + op + "'");
    }
    using (var command = new SqlCommand())
        Reject(() => Bind(command, "SELECT @DATE;", definitions["NoRules"], new ApisParameterValue("@DATE", "2026-10-23", "gt")),
            "Default rules reject greater-than");
    using (var command = new SqlCommand())
        Reject(() => Bind(command, "SELECT @DATE;", definitions["Restricted"], new ApisParameterValue("@DATE", "2026-10-23")),
            "Explicit operator restrictions are preserved");
    using (var command = new SqlCommand())
    {
        Bind(command, "SELECT @DATE;", definitions["Restricted"], new ApisParameterValue("@DATE", "2026-10-23", "ne"));
        Check((string)command.Parameters["@DATE_OPERATOR"].Value == "ne", "Explicitly allowed operator still works");
    }
    using (var command = new SqlCommand())
    {
        Bind(command, "SELECT @DATE;", definitions["Format"], new ApisParameterValue("@DATE", "2026-10-23"));
        Check(command.Parameters["@DATE"].SqlDbType == SqlDbType.NVarChar &&
              (string)command.Parameters["@DATE"].Value == "20261023", "Legacy formatted date remains text yyyyMMdd");
    }
    using (var command = new SqlCommand())
    {
        Bind(command, "SELECT @OWNER;", definitions["Text"], new ApisParameterValue("@OWNER", "AB"));
        Check((string)command.Parameters["@OWNER"].Value == "AB", "Text parameter without rules accepts equality");
    }
    using (var command = new SqlCommand())
    {
        Bind(command, "SELECT @DATE;", definitions["Optional"]);
        Check(command.Parameters["@DATE"].Value == DBNull.Value, "Empty optional parameter binds SQL NULL");
    }
    using (var command = new SqlCommand())
        Reject(() => Bind(command, "SELECT @DATE;", definitions["NoRules"]), "Missing required date is rejected");
    using (var command = new SqlCommand())
        Reject(() => Bind(command, "SELECT @DATE;", definitions["NoRules"], new ApisParameterValue("@DATE", "2026-09-32")),
            "Nonexistent date is rejected");

    const string optionalSql = "SELECT ITEMCODE FROM items WHERE OWNER=@OWNER /* SQL_PARAM_FILTERS */ ORDER BY ITEMCODE;";
    using (var command = new SqlCommand())
    {
        var sql = Bind(command, optionalSql, definitions["OptionalFilters"], new("@OWNER", "ABC"),
            new("@CODE", "0000001"), new("@NAME", "fragment", "like", "or"));
        Check(sql.Contains("AND (ITEMCODE = @CODE OR ITEMNAME LIKE CASE") && sql.Contains("END) ORDER BY"),
            "SQL optional OR filters stay grouped under the mandatory WHERE condition");
        Check(sql.Contains("ELSE '%' + @NAME + '%' END") && (string)command.Parameters["@NAME"].Value == "fragment",
            "Existing SQL Server LIKE syntax and bound values are preserved");
    }
    using (var command = new SqlCommand())
    {
        var sql = Bind(command, optionalSql, definitions["OptionalFilters"], new ApisParameterValue("@OWNER", "ABC"));
        Check(!sql.Contains("SQL_PARAM_FILTERS") && !sql.Contains("AND ("),
            "Unselected SQL optional filters add no predicate");
    }
    var orderedFilters = new[] { new ApisParameterValue("@OWNER", "ABC"), new("@NAME", "fragment", "like"), new("@CODE", "0000001", Connector: "or") };
    using (var command = new SqlCommand())
    {
        var sql = Bind(command, optionalSql, definitions["OptionalFilters"], orderedFilters);
        Check(sql.IndexOf("ITEMNAME LIKE", StringComparison.Ordinal) < sql.IndexOf("ITEMCODE = @CODE", StringComparison.Ordinal) &&
            sql.Contains("OR ITEMCODE = @CODE)"), "Optional filters use the user's row order rather than metadata order");
    }
    using (var cache = new MemoryCache(new MemoryCacheOptions()))
    {
        var model = new DATA_EXCHANGE_HUB.Pages.Apis.IndexModel(Service(fixtureRoot), null!, null!, null!, null!, cache, new HostEnvironment(fixtureRoot))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "TEST\\reader")], "test"))
            } },
            Parameters = orderedFilters.ToList()
        };
        typeof(DATA_EXCHANGE_HUB.Pages.Apis.IndexModel).GetProperty(nameof(model.SelectedQuery))!.SetValue(model, definitions["OptionalFilters"]);
        Check(model.ActiveOptionalParameters.Select(parameter => parameter.Name).SequenceEqual(["@NAME", "@CODE"]),
            "The rendered optional filter rows preserve the executed order");
        var cacheKey = typeof(DATA_EXCHANGE_HUB.Pages.Apis.IndexModel).GetMethod("BuildResultCacheKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var firstKey = (string)cacheKey.Invoke(model, [definitions["OptionalFilters"], orderedFilters])!;
        var secondKey = (string)cacheKey.Invoke(model, [definitions["OptionalFilters"], new[] { orderedFilters[0], orderedFilters[2], orderedFilters[1] }])!;
        Check(firstKey != secondKey, "Different AND / OR row orders cannot reuse the same cached result");
    }

    var actualQuery = Service(root).ListQueries().Single(query => query.Name == "EE_MANH_ADJUSTIMENT");
    Check(actualQuery.Parameters.Single().AllowedOperators.SequenceEqual(["eq"]), "Actual EE query permits its required equality operator");
    foreach (var (cultureName, input) in new[] { ("lt-LT", "2026-10-23"), ("en-GB", "23/10/2026") })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        using var command = new SqlCommand();
        Bind(command, File.ReadAllText(Path.Combine(root, actualQuery.RelativePath)), actualQuery, new ApisParameterValue("@DATE", input));
        Check(command.Parameters["@DATE"].SqlDbType == SqlDbType.Date &&
              (DateTime)command.Parameters["@DATE"].Value == new DateTime(2026, 10, 23), "Actual EE query binds date for " + cultureName);
    }

    if (args.Contains("--sql"))
    {
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true).Build();
        var regressionSql = EeAdjustmentChecks.WithInlineSources(File.ReadAllText(Path.Combine(root, actualQuery.RelativePath)));
        Check(!regressionSql.Contains("[BLSTXSTAT1]", StringComparison.OrdinalIgnoreCase),
            "The timestamp regression uses inline sources without reading or changing application tables");
        File.WriteAllText(Path.Combine(fixtureQueries, "EE_DateRegression.sql"), regressionSql);
        var regressionService = Service(fixtureRoot, configuration);
        var regressionQuery = regressionService.ListQueries().Single(query => query.Name == "EE_DateRegression");
        foreach (var (cultureName, input) in new[] { ("lt-LT", "2026-11-19"), ("en-GB", "19/11/2026") })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var result = await regressionService.ExecuteAsync(regressionQuery.Id, [new("@DATE", input)], new HashSet<string>(["EE"]));
            Check(result.Rows.Count == 6 && result.Rows.GroupBy(row => row["SOURCE"]).All(group =>
                group.Count() == 3 && group.Select(row => row["ITEM"]).Order().SequenceEqual(new[] { "last", "middle", "start" })),
                "Nine fractional digits, inclusive midnight and exclusive next day in both UNION branches for " + cultureName);
            Check(result.Rows.All(row => DateTime.Parse(row["ACTION_DATE"]!, CultureInfo.CurrentCulture).Date == new DateTime(2026, 11, 19)),
                "ACTION_DATE handles all nine fractional digits for " + cultureName);
        }
        foreach (var (cultureName, input) in new[] { ("lt-LT", "2026-10-23"), ("en-GB", "23/10/2026") })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var result = await Service(root, configuration).ExecuteAsync(actualQuery.Id, [new("@DATE", input)], new HashSet<string>(["EE"]));
            Check(result.Rows.Count > 0 && result.Rows.Select(row => row["SOURCE"]).Distinct().Count() == 2,
                "Actual APIS ExecuteAsync returns both sources for " + cultureName + ": " + result.Rows.Count + " rows");
        }
        foreach (var (cultureName, input) in new[] { ("lt-LT", "2026-11-19"), ("en-GB", "19/11/2026") })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var result = await Service(root, configuration).ExecuteAsync(actualQuery.Id, [new("@DATE", input)], new HashSet<string>(["EE"]));
            Check(result.Columns.Contains("ACTION_DATE") && result.Rows.All(row =>
                DateTime.Parse(row["ACTION_DATE"]!, CultureInfo.CurrentCulture).Date == new DateTime(2026, 11, 19)),
                "Actual November 19 report has no conversion error and contains only the selected date for " + cultureName + ": " + result.Rows.Count + " rows");
        }
    }
    Console.WriteLine($"ALL {checks} APIS PARAMETER CHECKS PASSED.");
}
finally
{
    var fullPath = Path.GetFullPath(fixtureRoot);
    if (Path.GetDirectoryName(fullPath) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
        Path.GetFileName(fullPath).StartsWith("DATA_EXCHANGE_HUB_ApisParameters_", StringComparison.Ordinal))
        Directory.Delete(fullPath, recursive: true);
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
