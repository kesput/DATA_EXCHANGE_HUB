using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Hydra;

public sealed class IndexModel(
    IHydraQueryService hydraQueryService,
    IHydraAccessService hydraAccessService,
    IQueryCountryAccessService queryCountryAccessService,
    IExcelWorkbookService excelWorkbookService,
    Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache) : PageModel
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const int ResultPageSize = 10000;
    private IReadOnlySet<string> allowedCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [BindProperty]
    public int ResultPage { get; set; } = 1;

    public IReadOnlyList<HydraQueryDefinition> Queries { get; private set; } = [];

    public HydraQueryResult? Result { get; private set; }

    public HydraQueryDefinition? SelectedQuery { get; private set; }

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

    [BindProperty(SupportsGet = true)]
    public string QueryId { get; set; } = string.Empty;

    [BindProperty]
    public List<string> FilterColumns { get; set; } = [];

    [BindProperty]
    public List<string> FilterOperators { get; set; } = [];

    [BindProperty]
    public List<string> FilterValues { get; set; } = [];

    [BindProperty]
    public List<string> FilterConnectors { get; set; } = [];

    public IReadOnlyList<ExchangeTableFilter> ActiveFilters { get; private set; } = [];

    public IReadOnlyList<ExchangeTableFilter> FilterRows =>
        ActiveFilters.Count > 0
            ? ActiveFilters
            : [new ExchangeTableFilter(string.Empty, ExchangeTableFilter.EqualsOperator, string.Empty, ExchangeTableFilter.AndConnector)];

public IReadOnlyList<HydraRequiredFilterRow> RequiredFilterRows =>
    SelectedQuery is null
        ? []
        : SelectedQuery.Filters
            .Where(filter => filter.IsRequired)
            .Select(filter => new HydraRequiredFilterRow(
                filter,
                ActiveFilters.FirstOrDefault(activeFilter =>
                    activeFilter.ColumnName.Equals(filter.Name, StringComparison.OrdinalIgnoreCase))?.Operator ?? ExchangeTableFilter.EqualsOperator,
                ActiveFilters.FirstOrDefault(activeFilter =>
                    activeFilter.ColumnName.Equals(filter.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty))
            .ToArray();

    public IReadOnlyList<ExchangeTableFilter> OptionalFilterRows
    {
        get
        {
            if (SelectedQuery is null)
            {
                return FilterRows;
            }

            var requiredNames = SelectedQuery.Filters
                .Where(filter => filter.IsRequired)
                .Select(filter => filter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var optionalRows = ActiveFilters
                .Where(filter => !requiredNames.Contains(filter.ColumnName))
                .ToArray();

            return optionalRows.Length > 0
                ? optionalRows
                : [new ExchangeTableFilter(string.Empty, ExchangeTableFilter.EqualsOperator, string.Empty, ExchangeTableFilter.AndConnector)];
        }
    }

    public string ActiveArea => SelectedQuery?.Area ?? "Handling";

    public IReadOnlyList<HydraPillar> Pillars { get; } =
    [
        new("H", "Handling", "Hydra.Pillar.Handling.Title", "Hydra.Pillar.Handling.Body"),
        new("Y", "Yield", "Hydra.Pillar.Yield.Title", "Hydra.Pillar.Yield.Body"),
        new("D", "Demand", "Hydra.Pillar.Demand.Title", "Hydra.Pillar.Demand.Body"),
        new("R", "Results", "Hydra.Pillar.Results.Title", "Hydra.Pillar.Results.Body"),
        new("A", "Attendance", "Hydra.Pillar.Attendance.Title", "Hydra.Pillar.Attendance.Body")
    ];

    public IReadOnlyList<string> Workstreams { get; } =
    [
        "Hydra.Workstream.Labeling",
        "Hydra.Workstream.Planning",
        "Hydra.Workstream.Productivity",
        "Hydra.Workstream.Wages"
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        return Page();
    }

    public async Task<IActionResult> OnPostExecuteAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        ActiveFilters = BuildFilters();
        SelectedQuery = Queries.FirstOrDefault(query =>
            query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));

        if (SelectedQuery is null)
        {
            ErrorMessage = "HYDRA SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            Result = await hydraQueryService.ExecuteAsync(
                SelectedQuery.Id,
                ActiveFilters,
                allowedCountries,
                HttpContext.RequestAborted);
            memoryCache.SetQueryResult(User, HttpContext.Session, "hydra", BuildResultCacheParts(SelectedQuery.Id), Result);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return Page();
    }

    public IActionResult OnPostResultPageAsync()
    {
        LoadQueries();
        ActiveFilters = BuildFilters();
        SelectedQuery = Queries.FirstOrDefault(query =>
            query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));

        if (SelectedQuery is null)
        {
            ErrorMessage = "HYDRA SQL uzklausa nerasta.";
            return Page();
        }

        if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "hydra", BuildResultCacheParts(SelectedQuery.Id), out HydraQueryResult? result) || result is null)
        {
            ErrorMessage = "Rezultatas neberastas cache. Paleiskite EXECUTE dar karta.";
            return Page();
        }

        Result = result;
        return Page();
    }
    public async Task<IActionResult> OnPostExportExcelAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        ActiveFilters = BuildFilters();
        SelectedQuery = Queries.FirstOrDefault(query =>
            query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));

        if (SelectedQuery is null)
        {
            ErrorMessage = "HYDRA SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "hydra", BuildResultCacheParts(SelectedQuery.Id), out HydraQueryResult? result) || result is null)
            {
                result = await hydraQueryService.ExecuteAsync(
                    SelectedQuery.Id,
                    ActiveFilters,
                    allowedCountries,
                HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "hydra", BuildResultCacheParts(SelectedQuery.Id), result);
            }
            var file = excelWorkbookService.CreateWorkbook(
                result.Columns,
                result.Rows,
                SelectedQuery.Name);

            AddExportSummaryHeaders(result.Rows.Count, result.Columns.Count);
            return File(file, ExcelContentType, BuildExcelFileName(SelectedQuery));
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostExportCsvAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        ActiveFilters = BuildFilters();
        SelectedQuery = Queries.FirstOrDefault(query =>
            query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));

        if (SelectedQuery is null)
        {
            ErrorMessage = "HYDRA SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "hydra", BuildResultCacheParts(SelectedQuery.Id), out HydraQueryResult? result) || result is null)
            {
                result = await hydraQueryService.ExecuteAsync(
                    SelectedQuery.Id,
                    ActiveFilters,
                    allowedCountries,
                HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "hydra", BuildResultCacheParts(SelectedQuery.Id), result);
            }
            var file = excelWorkbookService.CreateSemicolonCsv(
                result.Columns,
                result.Rows);

            AddExportSummaryHeaders(result.Rows.Count, result.Columns.Count);
            return File(file, CsvContentType, BuildCsvFileName(SelectedQuery));
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }

    public IReadOnlyList<HydraQueryDefinition> GetQueriesForArea(string area)
    {
        return Queries
            .Where(query => query.Area.Equals(area, StringComparison.OrdinalIgnoreCase))
            .OrderBy(query =>
                area.Equals("Handling", StringComparison.OrdinalIgnoreCase) &&
                query.Name.Equals("LT_VBX_INFO", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(query => query.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string GetAreaUrl(string area)
    {
        var firstQuery = GetQueriesForArea(area).FirstOrDefault();
        return firstQuery is null
            ? $"#hydra-{area}"
            : $"?QueryId={Uri.EscapeDataString(firstQuery.Id)}#hydra-{area}";
    }

    public static string GetFilterInputType(HydraFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "text",
            "datetime" => "text",
            "int" or "decimal" => "number",
            _ => "text"
        };
    }

    public static string GetFilterInputMode(HydraFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "int" => "numeric",
            "decimal" => "decimal",
            _ => "text"
        };
    }

    public static string GetFilterInputLanguage(HydraFilterDefinition definition)
    {
        return definition.DataType is "date" or "datetime"
            ? "en-CA"
            : string.Empty;
    }

    public static string FormatFilterInputValue(HydraFilterDefinition definition, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParse(value, out var dateValue))
        {
            return dateValue.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (definition.DataType.Equals("datetime", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParse(value, out var dateTimeValue))
        {
            return dateTimeValue.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }

    public static string GetOperatorLabel(string filterOperator)
    {
        return filterOperator switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "!=",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.InOperator => "IN",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => filterOperator
        };
    }

    private void LoadQueries()
    {
        Queries = hydraQueryService.ListQueries(allowedCountries);
        if (string.IsNullOrWhiteSpace(QueryId))
        {
            QueryId = Queries.FirstOrDefault()?.Id ?? string.Empty;
        }

        SelectedQuery = Queries.FirstOrDefault(query =>
            query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));
    }

    private IActionResult DenyPage()
    {
        ErrorMessage = "Siam Windows vartotojui HYDRA aplikacijos USER salies teise nesuteikta.";
        return Page();
    }

    private async Task<bool> CanOpenAsync()
    {
        try
        {
            var hasAccess = await hydraAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
            allowedCountries = hasAccess
                ? await queryCountryAccessService.GetUserCountriesAsync(User, "HYDRA", "DataExchangeSql", HttpContext.RequestAborted)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return hasAccess && allowedCountries.Count > 0;
        }
        catch
        {
            return false;
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
    }    private IEnumerable<string> BuildResultCacheParts(string queryId)
    {
        yield return queryId;
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

    private static string BuildCsvFileName(HydraQueryDefinition query)
    {
        return Path.ChangeExtension(BuildExcelFileName(query), ".csv")!;
    }

    private static string BuildExcelFileName(HydraQueryDefinition query)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var safeName = string.Concat(query.Name.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        return $"HYDRA_{safeName}_{timestamp}.xlsx";
    }
}

public sealed record HydraRequiredFilterRow(
    HydraFilterDefinition Definition,
    string Operator,
    string Value);

public sealed record HydraPillar(
    string Letter,
    string Area,
    string TitleKey,
    string BodyKey);







