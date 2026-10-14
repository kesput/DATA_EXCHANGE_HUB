using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.ClientInfo;

public sealed class IndexModel(
    IClientInfoService clientInfoService,
    IExcelWorkbookService excelWorkbookService,
    Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache) : PageModel
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const int ResultPageSize = 10000;

    [BindProperty]
    public List<string> FilterColumns { get; set; } = [];

    [BindProperty]
    public List<string> FilterOperators { get; set; } = [];

    [BindProperty]
    public List<string> FilterValues { get; set; } = [];

    [BindProperty]
    public List<string> FilterConnectors { get; set; } = [];

    [BindProperty]
    public int ResultPage { get; set; } = 1;

    public IReadOnlyList<HydraFilterDefinition> FilterDefinitions { get; private set; } = [];

    public IReadOnlyList<ExchangeTableFilter> ActiveFilters { get; private set; } = [];

    public HydraFilterDefinition? RequiredFilter =>
        FilterDefinitions.FirstOrDefault(filter => filter.IsRequired);

    public string RequiredFilterName => RequiredFilter?.Name ?? "FIRMID";

    public IReadOnlyList<string> RequiredFilterOperators =>
        RequiredFilter?.AllowedOperators ?? ["eq", "in"];

    public IReadOnlyList<HydraFilterDefinition> OptionalFilters =>
        FilterDefinitions.Where(filter => !filter.IsRequired).ToArray();

    public ClientInfoRequiredFilterRow? RequiredFilterRow =>
        RequiredFilter is null
            ? null
            : new ClientInfoRequiredFilterRow(
                RequiredFilter,
                ActiveFilters.FirstOrDefault(filter =>
                    filter.ColumnName.Equals(RequiredFilter.Name, StringComparison.OrdinalIgnoreCase))?.Operator ?? ExchangeTableFilter.EqualsOperator,
                ActiveFilters.FirstOrDefault(filter =>
                    filter.ColumnName.Equals(RequiredFilter.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty);

    public IReadOnlyList<ExchangeTableFilter> OptionalFilterRows
    {
        get
        {
            var requiredName = RequiredFilter?.Name ?? string.Empty;
            var optionalRows = ActiveFilters
                .Where(filter => !filter.ColumnName.Equals(requiredName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return optionalRows.Length > 0
                ? optionalRows
                : [];
        }
    }

    public ClientInfoResult? Result { get; private set; }

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

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadFilterDefinitionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostExecuteAsync()
    {
        await LoadFilterDefinitionsAsync();
        ActiveFilters = BuildFilters();
        try
        {
            Result = await clientInfoService.ExecuteAsync(
                ActiveFilters,
                HttpContext.RequestAborted);
            memoryCache.SetQueryResult(User, HttpContext.Session, "client-info", BuildResultCacheParts(), Result);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResultPageAsync()
    {
        await LoadFilterDefinitionsAsync();
        ActiveFilters = BuildFilters();

        if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "client-info", BuildResultCacheParts(), out ClientInfoResult? result) || result is null)
        {
            ErrorMessage = "Rezultatas neberastas cache. Paleiskite EXECUTE dar karta.";
            return Page();
        }

        Result = result;
        return Page();
    }
    public async Task<IActionResult> OnPostExportExcelAsync()
    {
        await LoadFilterDefinitionsAsync();
        ActiveFilters = BuildFilters();
        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "client-info", BuildResultCacheParts(), out ClientInfoResult? result) || result is null)
            {
                result = await clientInfoService.ExecuteAsync(
                    ActiveFilters,
                    HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "client-info", BuildResultCacheParts(), result);
            }
            var file = excelWorkbookService.CreateWorkbook(
                result.Columns,
                result.Rows,
                "Klientu info");

            AddExportSummaryHeaders(result.Rows.Count, result.Columns.Count);
            return File(file, ExcelContentType, BuildExcelFileName());
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }    public async Task<IActionResult> OnPostExportCsvAsync()
    {
        await LoadFilterDefinitionsAsync();
        ActiveFilters = BuildFilters();
        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "client-info", BuildResultCacheParts(), out ClientInfoResult? result) || result is null)
            {
                result = await clientInfoService.ExecuteAsync(
                    ActiveFilters,
                    HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "client-info", BuildResultCacheParts(), result);
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
    }    private IEnumerable<string> BuildResultCacheParts()
    {
        for (var index = 0; index < ActiveFilters.Count; index++)
        {
            var filter = ActiveFilters[index];
            yield return $"{index}|{filter.ColumnName}|{filter.Operator}|{filter.Value}|{filter.Connector}|{filter.IsActive}";
        }
    }
    private void AddExportSummaryHeaders(int rowCount, int columnCount)
    {
        Response.Headers.Append("X-Export-Row-Count", rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Response.Headers.Append("X-Export-Column-Count", columnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string BuildCsvFileName()
    {
        return Path.ChangeExtension(BuildExcelFileName(), ".csv")!;
    }

    private static string BuildExcelFileName()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"CLIENT_INFO_{timestamp}.xlsx";
    }

    public static string GetOperatorLabel(string operatorName)
    {
        return operatorName switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "!=",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.InOperator => "IN",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => operatorName
        };
    }

    public static string GetFilterInputType(HydraFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "text",
            "datetime" => "text",
            "number" => "number",
            _ => "text"
        };
    }

    public static string GetFilterInputMode(HydraFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "number" => "decimal",
            _ => "text"
        };
    }

    private async Task LoadFilterDefinitionsAsync()
    {
        try
        {
            FilterDefinitions = await clientInfoService.GetFilterDefinitionsAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private IReadOnlyList<ExchangeTableFilter> BuildFilters()
    {
        var count = new[]
        {
            FilterColumns.Count,
            FilterOperators.Count,
            FilterValues.Count,
            FilterConnectors.Count
        }.Max();
        var filters = new List<ExchangeTableFilter>();

        for (var index = 0; index < count; index++)
        {
            var filter = new ExchangeTableFilter(
                GetFormValue(FilterColumns, index),
                GetFormValue(FilterOperators, index, ExchangeTableFilter.EqualsOperator),
                GetFormValue(FilterValues, index),
                GetFormValue(FilterConnectors, index, ExchangeTableFilter.AndConnector));

            if (filter.IsActive)
            {
                filters.Add(filter);
            }
        }

        return filters;
    }

    private static string GetFormValue(IReadOnlyList<string> values, int index, string fallback = "")
    {
        return index >= 0 && index < values.Count
            ? values[index]?.Trim() ?? fallback
            : fallback;
    }
}

public sealed record ClientInfoRequiredFilterRow(
    HydraFilterDefinition Definition,
    string Operator,
    string Value);






