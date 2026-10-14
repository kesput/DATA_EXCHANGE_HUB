using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace DATA_EXCHANGE_HUB.Pages.Oracul;

public sealed class IndexModel(
    IOraculService oraculService,
    IExcelWorkbookService excelWorkbookService,
    IUserNameResolver userNameResolver,
    IStringLocalizer<DATA_EXCHANGE_HUB.SharedResource> localizer,
    Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache) : PageModel
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const int ResultPageSize = 10000;

    [BindProperty]
    public int DayNumber { get; set; }

    [BindProperty]
    public string QueryMode { get; set; } = "weekday";

    [BindProperty]
    public string DateText { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [BindProperty]
    public string ReportName { get; set; } = string.Empty;

    [BindProperty]
    public int ResultPage { get; set; } = 1;

    public IReadOnlyList<string> Reports { get; private set; } = [];

    public string UserName { get; private set; } = string.Empty;

    public bool HasAccess { get; private set; }

    public bool HasExecuted { get; private set; }

    public OraculQueryResult? Result { get; private set; }

    public string? ErrorMessage { get; private set; }
    public int ResultPageValue => Math.Max(1, ResultPage);

    public int ResultTotalRows => Result?.Rows.Count ?? 0;

    public int ResultTotalPages => Math.Max(1, (int)Math.Ceiling(ResultTotalRows / (double)ResultPageSize));

    public bool HasResultPaging => ResultTotalRows > ResultPageSize;

    public int ResultPageStartRow => ResultTotalRows == 0 ? 0 : ((Math.Min(ResultPageValue, ResultTotalPages) - 1) * ResultPageSize) + 1;

    public int ResultPageEndRow => Math.Min(ResultTotalRows, Math.Min(ResultPageValue, ResultTotalPages) * ResultPageSize);

    public IReadOnlyList<IReadOnlyDictionary<string, string?>> DisplayRows => Result is null
        ? []
        : Result.Rows
            .Skip((Math.Min(ResultPageValue, ResultTotalPages) - 1) * ResultPageSize)
            .Take(ResultPageSize)
            .ToArray();

    public string BruttoTotalText => FormatMetricValue(SumResultColumn("BRUTTO_T"));

    public string EplTotalText => FormatMetricValue(SumResultColumn("PLL"));

    public bool IsDateMode => QueryMode.Equals("date", StringComparison.OrdinalIgnoreCase);

    public async Task OnGetAsync()
    {
        ApplyDefaultDayNumber();
        ApplyDefaultDateText();
        await LoadAccessAsync();
        if (HasAccess)
        {
            await LoadReportsAsync();
            ApplyDefaultReportIfMissing();
        }
    }

    public async Task<IActionResult> OnPostExecuteAsync()
    {
        ApplyDefaultDayNumber();
        ApplyDefaultDateText();
        await LoadAccessAsync();
        HasExecuted = true;

        if (!HasAccess)
        {
            ErrorMessage = localizer["Oracul.AccessDeniedError"];
            return Page();
        }

        await LoadReportsAsync();
        ApplyDefaultReportIfMissing();

        if (!ValidateParameters(out var selectedDate))
        {
            return Page();
        }

        try
        {
            Result = await oraculService.ExecuteAsync(
                IsDateMode ? null : DayNumber,
                IsDateMode ? selectedDate : null,
                ReportName,
                HttpContext.RequestAborted);
            memoryCache.SetQueryResult(User, HttpContext.Session, "oracul", BuildResultCacheParts(selectedDate), Result);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResultPageAsync()
    {
        ApplyDefaultDayNumber();
        ApplyDefaultDateText();
        await LoadAccessAsync();
        HasExecuted = true;

        if (!HasAccess)
        {
            ErrorMessage = localizer["Oracul.AccessDeniedError"];
            return Page();
        }

        await LoadReportsAsync();
        ApplyDefaultReportIfMissing();

        if (!ValidateParameters(out var selectedDate))
        {
            return Page();
        }

        if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "oracul", BuildResultCacheParts(selectedDate), out OraculQueryResult? result) || result is null)
        {
            ErrorMessage = "Rezultatas neberastas cache. Paleiskite EXECUTE dar karta.";
            return Page();
        }

        Result = result;
        return Page();
    }
    public async Task<IActionResult> OnPostExportExcelAsync()
    {
        ApplyDefaultDayNumber();
        ApplyDefaultDateText();
        await LoadAccessAsync();

        if (!HasAccess)
        {
            ErrorMessage = localizer["Oracul.AccessDeniedError"];
            return Page();
        }

        await LoadReportsAsync();
        ApplyDefaultReportIfMissing();

        if (!ValidateParameters(out var selectedDate))
        {
            return Page();
        }

        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "oracul", BuildResultCacheParts(selectedDate), out OraculQueryResult? result) || result is null)
            {
                result = await oraculService.ExecuteAsync(
                    IsDateMode ? null : DayNumber,
                    IsDateMode ? selectedDate : null,
                    ReportName,
                    HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "oracul", BuildResultCacheParts(selectedDate), result);
            }
            var file = excelWorkbookService.CreateWorkbook(
                result.Columns,
                result.Rows,
                "ORACUL");

            AddExportSummaryHeaders(result.Rows.Count, result.Columns.Count);
            return File(file, ExcelContentType, BuildExcelFileName());
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostExportCsvAsync()
    {
        ApplyDefaultDayNumber();
        ApplyDefaultDateText();
        await LoadAccessAsync();

        if (!HasAccess)
        {
            ErrorMessage = localizer["Oracul.AccessDeniedError"];
            return Page();
        }

        await LoadReportsAsync();
        ApplyDefaultReportIfMissing();

        if (!ValidateParameters(out var selectedDate))
        {
            return Page();
        }

        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "oracul", BuildResultCacheParts(selectedDate), out OraculQueryResult? result) || result is null)
            {
                result = await oraculService.ExecuteAsync(
                    IsDateMode ? null : DayNumber,
                    IsDateMode ? selectedDate : null,
                    ReportName,
                    HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "oracul", BuildResultCacheParts(selectedDate), result);
            }
            var file = excelWorkbookService.CreateSemicolonCsv(
                result.Columns,
                result.Rows);

            AddExportSummaryHeaders(result.Rows.Count, result.Columns.Count);
            return File(file, CsvContentType, BuildCsvFileName());
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }

    private async Task LoadReportsAsync()
    {
        try
        {
            Reports = await oraculService.GetReportsAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Reports = [];
            ErrorMessage = exception.Message;
        }
    }

    private async Task LoadAccessAsync()
    {
        UserName = userNameResolver.ResolveUserName(User);

        try
        {
            HasAccess = await oraculService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            HasAccess = false;
            ErrorMessage = exception.Message;
        }
    }

    private void ApplyDefaultDayNumber()
    {
        if (DayNumber is < 1 or > 7)
        {
            DayNumber = (int)DateTime.Today.DayOfWeek + 1;
        }
    }

    private void ApplyDefaultDateText()
    {
        if (string.IsNullOrWhiteSpace(DateText))
        {
            DateText = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private void ApplyDefaultReportIfMissing()
    {
        if (string.IsNullOrWhiteSpace(ReportName))
        {
            ReportName = Reports.FirstOrDefault() ?? string.Empty;
        }
    }

    private bool ValidateParameters(out DateTime? selectedDate)
    {
        selectedDate = null;
        QueryMode = IsDateMode ? "date" : "weekday";

        if (!IsDateMode && DayNumber is < 1 or > 7)
        {
            ErrorMessage = localizer["Oracul.Error.WeekdayRange"];
            return false;
        }

        if (IsDateMode)
        {
            if (!DateTime.TryParseExact(
                    DateText?.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                ErrorMessage = localizer["Oracul.Error.InvalidDate"];
                return false;
            }

            selectedDate = parsedDate.Date;
        }

        if (Reports.Count == 0)
        {
            ErrorMessage = localizer["Oracul.Error.EmptyReports"];
            return false;
        }

        if (!Reports.Contains(ReportName, StringComparer.OrdinalIgnoreCase))
        {
            ErrorMessage = localizer["Oracul.Error.InvalidReport"];
            return false;
        }

        return true;
    }

    private IEnumerable<string> BuildResultCacheParts(DateTime? selectedDate)
    {
        yield return QueryMode;
        yield return DayNumber.ToString(CultureInfo.InvariantCulture);
        yield return selectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        yield return ReportName ?? string.Empty;
    }
    private void AddExportSummaryHeaders(int rowCount, int columnCount)
    {
        Response.Headers.Append("X-Export-Row-Count", rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Response.Headers.Append("X-Export-Column-Count", columnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private string BuildCsvFileName()
    {
        return Path.ChangeExtension(BuildExcelFileName(), ".csv")!;
    }

    private string BuildExcelFileName()
    {
        var reportPart = ReportName;
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            reportPart = reportPart.Replace(invalidCharacter, '_');
        }

        var modePart = IsDateMode
            ? $"date{DateText.Replace("-", string.Empty, StringComparison.Ordinal)}"
            : $"day{DayNumber}";
        return $"ORACUL_{reportPart}_{modePart}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    }

    private decimal SumResultColumn(string columnName)
    {
        if (Result is null)
        {
            return 0;
        }

        var resultColumn = Result.Columns.FirstOrDefault(column =>
            NormalizeColumnName(column).Equals(
                NormalizeColumnName(columnName),
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resultColumn))
        {
            return 0;
        }

        decimal total = 0;
        foreach (var row in Result.Rows)
        {
            if (row.TryGetValue(resultColumn, out var rawValue) &&
                TryParseMetricValue(rawValue, out var value))
            {
                total += value;
            }
        }

        return total;
    }

    private static string NormalizeColumnName(string columnName)
    {
        return new string(columnName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool TryParseMetricValue(string? rawValue, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);

        var lastComma = normalized.LastIndexOf(',');
        var lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            normalized = lastComma > lastDot
                ? normalized.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal)
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (lastComma >= 0)
        {
            normalized = normalized.Replace(",", ".", StringComparison.Ordinal);
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string FormatMetricValue(decimal value)
    {
        return value.ToString("N2", CultureInfo.GetCultureInfo("lt-LT"));
    }
}





