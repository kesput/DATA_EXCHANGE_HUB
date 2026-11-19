using System.Globalization;
using System.Text;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var service = new ExcelWorkbookService();
string[] headers = ["Pavadinimas", "Pastabos", "Tu\u0161\u010dia"];
IReadOnlyDictionary<string,string?>[] rows = [new Dictionary<string,string?> {
    ["Pavadinimas"] = "\u0104\u010c\u0118\u0116\u012e\u0160\u0172\u016a\u017d \u0105\u010d\u0119\u0117\u012f\u0161\u0173\u016b\u017e \u0100\u0101 \u0122\u0123 \u0136\u0137 \u013b\u013c \u0145\u0146 \u00d5\u00f5", ["Pastabos"] = "a;\"b\"\r\nnext", ["Tu\u0161\u010dia"] = null }];
var utf8 = service.CreateSemicolonCsv(headers, rows);
var baltic = service.CreateSemicolonCsv(headers, rows, true);
Check(utf8.Take(3).SequenceEqual(new byte[] {0xef,0xbb,0xbf}), "Default UTF-8 has BOM");
Check(!baltic.Take(3).SequenceEqual(new byte[] {0xef,0xbb,0xbf}), "Windows-1257 has no BOM");
Check(baltic.Take(6).SequenceEqual(Encoding.ASCII.GetBytes("sep=;\r")), "Windows-1257 starts with separator instruction");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var decoded = Encoding.GetEncoding(1257).GetString(baltic);
Check(decoded.Contains(rows[0]["Pavadinimas"]!), "Baltic letters survive Windows-1257");
Check(decoded.StartsWith("sep=;\r\n\"Pavadinimas\";\"Pastabos\";\"Tu\u0161\u010dia\"\r\n"), "All headers quoted in Windows format");
Check(decoded.EndsWith(";\"\"\r\n"), "Null cells are quoted empty values");
Check(decoded.Contains("\"a;\"\"b\"\"\r\nnext\""), "CSV quoting preserved");
Check(CsvExportEncoding.ContentType(true) == "text/csv; charset=windows-1257", "Windows-1257 MIME charset");
Check(CsvExportEncoding.ContentType(false) == "text/csv; charset=utf-8", "UTF-8 MIME charset");
foreach (var language in new[]{"lt", "en"}) {
 CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
 try { service.CreateSemicolonCsv(["Value"], [new Dictionary<string,string?>{["Value"]="\U0001f642\u4e2d\u6587"}], true); throw new Exception("Expected encoding rejection"); }
 catch (InvalidOperationException ex) { Check(ex.Message.Contains(language=="en" ? "Clear" : "Nuimkite"), "Unsupported characters fail with actionable " + language + " message"); }
}
Check(Encoding.UTF8.GetString(service.CreateSemicolonCsv(["Value"], [new Dictionary<string,string?>{["Value"]="\U0001f642\u4e2d\u6587"}])).Contains("\U0001f642\u4e2d\u6587"), "UTF-8 retains all Unicode");
foreach (var module in new[]{"Apis","Arete","Hydra","Oracul","ClientInfo","Themis","DataExchange"}) {
 var type = typeof(ExcelWorkbookService).Assembly.GetType("DATA_EXCHANGE_HUB.Pages."+module+".IndexModel")!;
 var property = type.GetProperty("CsvWindows1257")!;
 Check(property.PropertyType == typeof(bool) && property.GetCustomAttributes(typeof(BindPropertyAttribute),false).Cast<BindPropertyAttribute>().Single().SupportsGet, module+" supports GET and POST binding");
}

IReadOnlyDictionary<string,string?>[] numericRows = [new Dictionary<string,string?> {
 ["QUANTITY"]="-300,00000", ["CODE"]="001,02", ["NAME"]="AB \"Kauno gr\u016bdai\"" }];
var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "quantity" };
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var numericCsv = Encoding.GetEncoding(1257).GetString(service.CreateSemicolonCsv(["QUANTITY","CODE","NAME"], numericRows, true, numericColumns, "lt-LT"));
Check(numericCsv.Contains("\"-300.00000\";\"001,02\";\"AB \"\"Kauno gr\u016bdai\"\"\""), "APIS numeric scale preserved, text commas and leading zeros intact, embedded quotes doubled");
Check(numericCsv == "sep=;\r\n\"QUANTITY\";\"CODE\";\"NAME\"\r\n\"-300.00000\";\"001,02\";\"AB \"\"Kauno gr\u016bdai\"\"\"\r\n", "Exact automatic CSV layout");
var unchanged = Encoding.UTF8.GetString(service.CreateSemicolonCsv(["QUANTITY","CODE","NAME"], numericRows, false, numericColumns, "lt-LT"));
Check(unchanged.Contains("-300,00000;001,02;"), "UTF-8 retains current formatting");
Check(CsvExportEncoding.InvariantNumber("-2,00000", "lt-LT") == "-2.00000", "LT cached number converted independently of current EN culture");
var ltNegative = CultureInfo.GetCultureInfo("lt-LT").NumberFormat.NegativeSign;
Check(CsvExportEncoding.InvariantNumber(ltNegative + "2,50000", "lt-LT") == "-2.50000", "LT native negative sign normalizes to Windows-compatible ASCII minus");
Check(CsvExportEncoding.InvariantNumber("-2.00000", "en-US") == "-2.00000", "EN SQL number unchanged");
Check(CsvExportEncoding.InvariantNumber("123456789012345678901234567890123,00000", "lt-LT") == "123456789012345678901234567890123.00000", "SQL high precision survives without decimal parsing");
var sourceType = typeof(DATA_EXCHANGE_HUB.Models.IrisReportSource);
Check(sourceType.GetProperty("CsvWindows1257")?.PropertyType == typeof(bool), "IRIS exposes persisted encoding for sender");
Console.WriteLine($"ALL {checks} CSV CHECKS PASSED");
