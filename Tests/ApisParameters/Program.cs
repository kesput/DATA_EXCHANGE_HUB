using System.Data;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
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
        ["Text"] = "-- SQL_PARAM_MUST: @OWNER|text||Owner"
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
        foreach (var (cultureName, input) in new[] { ("lt-LT", "2026-10-23"), ("en-GB", "23/10/2026") })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var result = await Service(root, configuration).ExecuteAsync(actualQuery.Id, [new("@DATE", input)], new HashSet<string>(["EE"]));
            Check(result.Rows.Count > 0 && result.Rows.Select(row => row["SOURCE"]).Distinct().Count() == 2,
                "Actual APIS ExecuteAsync returns both sources for " + cultureName + ": " + result.Rows.Count + " rows");
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
