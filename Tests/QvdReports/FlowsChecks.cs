using System.Globalization;
using Bondski.QvdLib;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

internal static class FlowsChecks
{
    private const string Query = "Sql/Apis/LT/QVD/Processes/LT_FLOWS_QVD_JOIN.sql";

    internal static async Task RunAsync(string projectRoot, string fixture,
        Action<string, string[], string[][]> write, Action<bool, string> check)
    {
        var data = Path.Combine(fixture, "flows-data");
        foreach (var folder in new[] { "FLOWS_DB", "EPL", "IBL", "AMOUNT" }) Directory.CreateDirectory(Path.Combine(data, folder));
        var queryPath = Path.Combine(fixture, Query);
        File.Copy(Path.Combine(projectRoot, Query), queryPath);
        var fields = new[] { "RECNO", "DORD", "OWNER", "INF_PREK", "DOCK", "PREF", "PREFSTOCK", "OPERATION", "MOVEMENT_TYPE", "OPER_STATUS", "INVOICEOWNER", "PAL_DAL", "BRUTTO_KG" };
        string[] Flow(string recno, string date, string dock, string owner = "001", string pref = "VB1",
            string operation = "OUTGOING OPERATION", string movement = "OUT", string status = "OK", string invoiceOwner = "OTHER") =>
            [recno, date, owner, "0000001", dock, pref, "WL", operation, movement, status, invoiceOwner, "1.25", "-2.5"];
        write(Path.Combine(data, "FLOWS_DB/2026.11.16_LT_FLOWS_DB.qvd"), fields, [Flow("A", "2026-11-16", "FIRST")]);
        write(Path.Combine(data, "FLOWS_DB/2026.11.17_LT_FLOWS_DB.qvd"), fields,
            [Flow("A", "2026-11-17", "SECOND"), Flow("A", "2026-11-16", "LAGGED"),
             Flow("D", "2026-11-17", "DUPLICATE"), Flow("N", "2026-11-17", "MISSING"),
             Flow("F", "2026-11-17", "QVU0322237"), Flow("E", "2026-11-17", "CALC", pref: "MTD", status: "CALC"),
             Flow("EX1", "2026-11-17", "EXCLUDED_INVOICE", invoiceOwner: "EOR"),
             Flow("EX2", "2026-11-17", "EXCLUDED_PREF", pref: "MTD"),
             Flow("EX3", "2026-11-17", "EXCLUDED_PARCEL", operation: "INCOMING OPERATION", movement: "PARCEL"),
             Flow("O", "2026-11-17", "OTHER_OWNER", owner: "002")]);
        foreach (var day in new[] { "16", "17" })
        {
            var value = day == "16" ? "10" : "20";
            var epl = new List<string[]> { new[] { "A", value, value } };
            var ibl = new List<string[]> { new[] { "A", "CATEGORY", value } };
            var amount = new List<string[]> { new[] { "A", value, value, value } };
            if (day == "17")
                foreach (var duplicate in new[] { "1", "2" })
                {
                    epl.Add(["D", duplicate, duplicate]);
                    ibl.Add(["D", "CATEGORY", duplicate]);
                    amount.Add(["D", duplicate, duplicate, duplicate]);
                }
            write(Path.Combine(data, $"EPL/2026.11.{day}_EPL.qvd"), ["RECNO", "CALC_PLL_DAL", "EPL_DAL"], epl.ToArray());
            write(Path.Combine(data, $"IBL/2026.11.{day}_EPL.qvd"), ["RECNO", "IBL_CAT_NEW", "PAL_DAL_IBL_EXT"], ibl.ToArray());
            write(Path.Combine(data, $"AMOUNT/2026.11.{day}_AMOUNT.qvd"),
                ["RECNO", "AMOUNT_IN_TOTAL_SUM", "AMOUNT_OUT_TOTAL_SUM", "AMOUNT_RETURN_TARE_SUM"], amount.ToArray());
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QvdReports:Sources:LT_FLOWS:RootPath"] = data,
            ["QvdReports:Sources:LT_FLOWS:Countries:0"] = "LT",
            ["QvdReports:Sources:LT_FLOWS:Modules:0"] = "Apis"
        }).Build();
        var service = new ApisQueryService(config, new HostEnvironment(fixture), Options.Create(new ApisOptions()), new AllowedQvdReports());
        Task<ApisQueryResult> Execute(string? owner, string start = "2026-11-16", ApisParameterValue[]? optional = null) => service.ExecuteAsync(Query,
            [new("@SNDATE", start), new("@ENDATE", "2026-11-17"), new("@OWNER", owner ?? "NULL"),
             new("@OPERATION", "OUTGOING OPERATION"), .. (optional ?? [])], new HashSet<string>(["LT"]));
        var definition = service.ListQueries().Single(query => query.Name == "LT_FLOWS_QVD_JOIN");
        check(definition.Parameters.Count(parameter => !parameter.IsRequired) == 4 &&
            definition.Parameters.Where(parameter => !parameter.IsRequired).All(parameter => parameter.AllowedOperators.SequenceEqual(["eq", "like", "in"])) &&
            definition.Parameters.Single(parameter => parameter.Name == "@OWNER").IsRequired &&
            definition.Parameters.Single(parameter => parameter.Name == "@OPERATION").IsRequired,
            "FLOWS exposes four optional filters and preserves the user's mandatory OWNER / OPERATION parameters");
        var result = await Execute("001");
        var byDock = result.Rows.ToDictionary(row => row["DOCK"]!);
        check(result.Rows.Count == 7 && byDock.Count == 7, "FLOWS sample filters keep the expected rows without multiplying JOINs");
        check(byDock["FIRST"]["EPL_DAL"] == "10" && byDock["SECOND"]["EPL_DAL"] == "20" && byDock["LAGGED"]["EPL_DAL"] == "10",
            "FLOWS joins use RECNO and DORD, including an operation dated before its QVD file");
        check(new[] { "EPL_MATCH_ROWS", "IBL_MATCH_ROWS", "AMOUNT_MATCH_ROWS" }.All(column => byDock["DUPLICATE"][column] == "2") &&
            new[] { "EPL_DAL", "IBL_CAT_NEW", "PAL_DAL_IBL_EXT", "AMOUNT_IN_TOTAL_SUM" }.All(column => string.IsNullOrEmpty(byDock["DUPLICATE"][column])),
            "Conflicting duplicate keys are flagged and their metrics are empty");
        check(byDock["MISSING"]["EPL_MATCH_ROWS"] == "0" && string.IsNullOrEmpty(byDock["MISSING"]["EPL_DAL"]),
            "A missing JOIN match stays visible and does not become a numeric zero");
        check(byDock["QVU0322237"]["OPERATION"] == "INTERNAL OPERATION" && byDock["QVU0322237"]["OPER_STATUS"] == "FAKE" && byDock.ContainsKey("CALC"),
            "FLOWS keeps the supplied document override and CALC filter exception");
        check(result.NumericColumns!.Contains("EPL_DAL") && result.Rows.All(row => row["INF_PREK"] == "0000001"),
            "FLOWS exports retain numeric metrics and leading zero product codes");
        check((await Execute("002")).Rows.Single()["DOCK"] == "OTHER_OWNER" && (await Execute(null)).Rows.Count == 8,
            "FLOWS exact owner filtering and optional all-owner selection work without SQL Server");
        check((await Execute("001", "2026-11-17")).Rows.Single(row => row["DOCK"] == "LAGGED")["EPL_MATCH_ROWS"] == "0",
            "An operation date outside the loaded period is reported as an unmatched key");
        check((await Execute("001", optional: [new("@INF_PREK", "0000001")])).Rows.Count == 7 &&
            (await Execute("001", optional: [new("@INF_PREK", "0000002")])).Rows.Count == 0,
            "Optional INF_PREK equality filters by the source alias");
        check((await Execute("001", optional: [new("@INF_PREK", "0000002, 0000001", "in")])).Rows.Count == 7,
            "Optional INF_PREK IN accepts comma-separated product codes");
        check((await Execute("001", optional: [new("@DOCK", "IRS", "like")])).Rows.Single()["DOCK"] == "FIRST" &&
            (await Execute("001", optional: [new("@DOCK", "S%ND", "like")])).Rows.Single()["DOCK"] == "SECOND",
            "Optional QVD LIKE supports fragments and preserves explicit wildcard patterns");
        check((await Execute("001", optional: [new("@DOCK", "FIRST"), new("@PREFSTOCK", "WL")])).Rows.Single()["DOCK"] == "FIRST",
            "Multiple optional filters can be combined with AND");
        check((await Execute("001", optional: [new("@DOCK", "FIRST"), new("@PREF", "VB1", Connector: "or")])).Rows.Count == 6,
            "Optional OR cannot bypass mandatory owner, operation or the base filters");
        check((await Execute("001", optional: [new("@INF_PREK", "")])).Rows.Count == 7,
            "An empty optional field does not filter or become mandatory");
        var constrained = new ConfigurationBuilder().AddConfiguration(config).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["QvdReports:MaxInputRows"] = "4"
        }).Build();
        var narrow = await new ApisQueryService(constrained, new HostEnvironment(fixture), Options.Create(new ApisOptions()), new AllowedQvdReports()).ExecuteAsync(Query,
            [new("@SNDATE", "2026-11-16"), new("@ENDATE", "2026-11-17"), new("@OWNER", "001"),
             new("@OPERATION", "OUTGOING OPERATION"), new("@DOCK", "FIRST")], new HashSet<string>(["LT"]));
        check(narrow.Rows.Single()["DOCK"] == "FIRST" && narrow.ReadStatistics is { RetainedRows: 4 } &&
            narrow.ReadStatistics.RetainedRowsBySource.Values.All(rows => rows == 1),
            "FLOWS source and dependent JOIN rows are filtered before the four-row memory budget");
        var empty = await Execute("001", optional: [new("@DOCK", "DOES_NOT_EXIST")]);
        check(empty.Rows.Count == 0 && empty.ReadStatistics?.RetainedRows == 0,
            "No matching parent rows also retains zero rows from every dependent QVD source");
    }

    internal static async Task RunFilteredRealAsync(string projectRoot, IConfiguration config, Action<bool, string> check)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var service = new ApisQueryService(config, new HostEnvironment(projectRoot), Options.Create(new ApisOptions()), new AllowedQvdReports());
        var result = await service.ExecuteAsync(Query,
            [new("@SNDATE", "2026-11-01"), new("@ENDATE", "2026-11-18"), new("@OWNER", "SNX"),
             new("@OPERATION", "OUTGOING OPERATION"), new("@PREFSTOCK", "VN1"), new("@INF_PREK", "M011292")],
            new HashSet<string>(["LT"]));
        var querySeconds = timer.Elapsed.TotalSeconds;
        var expectedCount = 0;
        double expectedPallets = 0;
        for (var day = new DateOnly(2026, 11, 1); day <= new DateOnly(2026, 11, 18); day = day.AddDays(1))
        {
            using var qvd = new QvdReader(Path.Combine(config["QvdReports:Sources:LT_FLOWS:RootPath"]!,
                "FLOWS_DB", day.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + "_LT_FLOWS_DB.qvd"));
            while (qvd.NextRow())
            {
                string Text(string field) => qvd[field].ToString() ?? "";
                if (Text("INF_PREK") != "M011292" || Text("PREFSTOCK") != "VN1" || Text("OWNER") != "SNX" ||
                    Text("OPERATION") != "OUTGOING OPERATION") continue;
                var pref = Text("PREF");
                var keep = Text("OPER_STATUS") == "CALC" || (pref.Length > 1 &&
                    (pref.StartsWith("VB", StringComparison.Ordinal) || Text("PREFSTOCK").StartsWith('W')) && pref != "MTD" &&
                    !new[] { "EOR", "IC", "PCCLT", "UVSLT" }.Contains(Text("INVOICEOWNER")));
                if (!keep) continue;
                expectedCount++;
                var value = qvd["PAL_DAL"];
                expectedPallets += value.Type switch
                {
                    Bondski.QvdLib.ValueType.Int or Bondski.QvdLib.ValueType.DualInt => value.Int,
                    Bondski.QvdLib.ValueType.Double or Bondski.QvdLib.ValueType.DualDouble => value.Double,
                    _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0
                };
            }
        }
        check(result.Rows.Count == expectedCount && expectedCount > 0,
            "Real November 1-18 SNX / VN1 / M011292 query matches an independent source scan");
        var actualPallets = result.Rows.Sum(row => double.TryParse(row["PAL_DAL"], NumberStyles.Float, CultureInfo.CurrentCulture, out var n) ? n : 0);
        check(Math.Abs(actualPallets - expectedPallets) < 0.000001, "Real filtered monthly JOIN preserves the source pallet total");
        check(result.ReadStatistics is { Files: 72 } stats && stats.RetainedRowsBySource["flows"] == expectedCount &&
            stats.RetainedRows < stats.ScannedRows / 100,
            "The 72-file monthly query retains only a small matching subset before JOINs");
        Console.WriteLine($"FILTERED FLOWS rows={result.Rows.Count}; columns={result.Columns.Count}; seconds={querySeconds:F1}");
        Console.WriteLine("FILTERED FLOWS statistics: " + System.Text.Json.JsonSerializer.Serialize(result.ReadStatistics));
    }

    internal static async Task RunRealAsync(string projectRoot, IConfiguration config, Action<bool, string> check)
    {
        var service = new ApisQueryService(config, new HostEnvironment(projectRoot), Options.Create(new ApisOptions()), new AllowedQvdReports());
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.ExecuteAsync(Query,
            [new("@SNDATE", "2026-11-17"), new("@ENDATE", "2026-11-17"), new("@OWNER", "NULL"), new("@OPERATION", "OUTGOING OPERATION")],
            new HashSet<string>(["LT"]));
        using var qvd = new QvdReader(Path.Combine(config["QvdReports:Sources:LT_FLOWS:RootPath"]!, "FLOWS_DB/2026.11.17_LT_FLOWS_DB.qvd"));
        var expectedCount = 0;
        double expectedPallets = 0;
        while (qvd.NextRow())
        {
            string Text(string field) => qvd[field].ToString() ?? "";
            var pref = Text("PREF");
            var incoming = Text("OPERATION") == "INCOMING OPERATION";
            var keep = Text("OPER_STATUS") == "CALC" || (pref.Length > 1 &&
                (pref.StartsWith("VB", StringComparison.Ordinal) || Text("PREFSTOCK").StartsWith('W') || (!pref.StartsWith("VB", StringComparison.Ordinal) && incoming)) &&
                pref != "MTD" && !new[] { "EOR", "IC", "PCCLT", "UVSLT" }.Contains(Text("INVOICEOWNER")) && !(Text("MOVEMENT_TYPE") == "PARCEL" && incoming));
            if (!keep || Text("OPERATION") != "OUTGOING OPERATION") continue;
            expectedCount++;
            var value = qvd["PAL_DAL"];
            expectedPallets += value.Type switch
            {
                Bondski.QvdLib.ValueType.Int or Bondski.QvdLib.ValueType.DualInt => value.Int,
                Bondski.QvdLib.ValueType.Double or Bondski.QvdLib.ValueType.DualDouble => value.Double,
                _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0
            };
        }
        check(result.Rows.Count == expectedCount && expectedCount > 0, "Real FLOWS four-file JOIN preserves the filtered source row count");
        var actualPallets = result.Rows.Sum(row => double.TryParse(row["PAL_DAL"], NumberStyles.Float, CultureInfo.CurrentCulture, out var n) ? n : 0);
        check(Math.Abs(actualPallets - expectedPallets) < 0.000001, "Real FLOWS JOIN preserves the source pallet total");
        foreach (var column in new[] { "EPL_MATCH_ROWS", "IBL_MATCH_ROWS", "AMOUNT_MATCH_ROWS" })
            Console.WriteLine($"REAL FLOWS {column}: missing={result.Rows.Count(row => row[column] == "0")}, duplicates={result.Rows.Count(row => long.Parse(row[column]!) > 1)}");
        Console.WriteLine($"REAL FLOWS rows={result.Rows.Count}; columns={result.Columns.Count}; seconds={timer.Elapsed.TotalSeconds:F1}");
    }
}
