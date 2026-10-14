using System.Globalization;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Arete;

public sealed class IndexModel(
    IAreteQueryService areteQueryService,
    IAreteAccessService areteAccessService,
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

    public IReadOnlyList<AreteQueryDefinition> Queries { get; private set; } = [];

    public AreteQueryResult? Result { get; private set; }

    public AreteQueryDefinition? SelectedQuery { get; private set; }

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

    [BindProperty(SupportsGet = true)]
    public string AreteArea { get; set; } = string.Empty;

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

public IReadOnlyList<AreteFilterDefinition> QueryParameters =>
    SelectedQuery is null ? [] : SelectedQuery.Filters.Where(filter => filter.IsQueryParameter).ToArray();

    public IReadOnlyList<AreteFilterDefinition> FilterDefinitions =>
    SelectedQuery is null ? [] : SelectedQuery.Filters.Where(filter => !filter.IsQueryParameter).ToArray();

public IReadOnlyList<AreteRequiredFilterRow> RequiredFilterRows =>
    SelectedQuery is null
        ? []
        : FilterDefinitions
            .Where(filter => filter.IsRequired)
            .Select(filter => new AreteRequiredFilterRow(
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

            var requiredNames = FilterDefinitions
                .Where(filter => filter.IsRequired)
                .Select(filter => filter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filterNames = FilterDefinitions
                .Select(filter => filter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var optionalRows = ActiveFilters
                .Where(filter => filterNames.Contains(filter.ColumnName) && !requiredNames.Contains(filter.ColumnName))
                .ToArray();

            return optionalRows.Length > 0
                ? optionalRows
                : [new ExchangeTableFilter(string.Empty, ExchangeTableFilter.EqualsOperator, string.Empty, ExchangeTableFilter.AndConnector)];
        }
    }

    public string ActiveArea => SelectedQuery?.Area ?? GetKnownArea(AreteArea) ?? "Tracking";

    public IReadOnlyList<AretePillar> Pillars { get; } =
    [
        new("A", "Assurance", "Arete.Pillar.Assurance.Title", "Arete.Pillar.Assurance.Body"),
        new("R", "Reporting", "Arete.Pillar.Reporting.Title", "Arete.Pillar.Reporting.Body"),
        new("E", "Excellence", "Arete.Pillar.Excellence.Title", "Arete.Pillar.Excellence.Body"),
        new("T", "Tracking", "Arete.Pillar.Tracking.Title", "Arete.Pillar.Tracking.Body"),
        new("E", "Environment", "Arete.Pillar.Environment.Title", "Arete.Pillar.Environment.Body")
    ];

    public IReadOnlyList<string> Workstreams { get; } =
    [
        "Arete.Workstream.Labeling",
        "Arete.Workstream.Planning",
        "Arete.Workstream.Productivity",
        "Arete.Workstream.Wages"
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
            ErrorMessage = "ARETE SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            Result = await areteQueryService.ExecuteAsync(
                SelectedQuery.Id,
                ActiveFilters,
                allowedCountries,
                HttpContext.RequestAborted);
            memoryCache.SetQueryResult(User, HttpContext.Session, "arete", BuildResultCacheParts(SelectedQuery.Id), Result);
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
            ErrorMessage = "ARETE SQL uzklausa nerasta.";
            return Page();
        }

        if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "arete", BuildResultCacheParts(SelectedQuery.Id), out AreteQueryResult? result) || result is null)
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
            ErrorMessage = "ARETE SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "arete", BuildResultCacheParts(SelectedQuery.Id), out AreteQueryResult? result) || result is null)
            {
                result = await areteQueryService.ExecuteAsync(
                    SelectedQuery.Id,
                    ActiveFilters,
                    allowedCountries,
                HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "arete", BuildResultCacheParts(SelectedQuery.Id), result);
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
            ErrorMessage = "ARETE SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            if (!memoryCache.TryGetQueryResult(User, HttpContext.Session, "arete", BuildResultCacheParts(SelectedQuery.Id), out AreteQueryResult? result) || result is null)
            {
                result = await areteQueryService.ExecuteAsync(
                    SelectedQuery.Id,
                    ActiveFilters,
                    allowedCountries,
                HttpContext.RequestAborted);
                memoryCache.SetQueryResult(User, HttpContext.Session, "arete", BuildResultCacheParts(SelectedQuery.Id), result);
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

    public IReadOnlyList<AreteQueryDefinition> GetQueriesForArea(string area)
    {
        return Queries
            .Where(query => query.Area.Equals(area, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public string GetParameterValue(AreteFilterDefinition definition)
    {
        return ActiveFilters.FirstOrDefault(filter =>
            filter.ColumnName.Equals(definition.Name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    public static bool IsDateParameter(AreteFilterDefinition definition)
    {
        return definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase);
    }

    public string GetDateDisplayFormat()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "dd/MM/yyyy"
            : "yyyy-MM-dd";
    }

    public string GetDateDisplayPlaceholder()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "dd/mm/yyyy"
            : "yyyy-mm-dd";
    }

    public string GetParameterDisplayValue(AreteFilterDefinition definition)
    {
        var value = GetParameterValue(definition);
        if (!IsDateParameter(definition) || string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return TryParseDateForDisplay(value, out var date)
            ? date.ToString(GetDateDisplayFormat(), CultureInfo.InvariantCulture)
            : value;
    }

    public string GetParameterNativeDateValue(AreteFilterDefinition definition)
    {
        var value = GetParameterValue(definition);
        return TryParseDateForDisplay(value, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static bool TryParseDateForDisplay(string value, out DateTime date)
    {
        return DateTime.TryParseExact(value, ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
               DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static string? GetParameterPattern(AreteFilterDefinition definition)
    {
        return definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase)
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? @"\d{2}/\d{2}/\d{4}" : @"\d{4}-\d{2}-\d{2}")
            : null;
    }

    public static string? GetParameterTitle(AreteFilterDefinition definition)
    {
        if (!definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var format = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "dd/MM/yyyy"
            : "yyyy-MM-dd";
        return $"Data turi buti formatu {format}.";
    }

    public static string GetParameterPlaceholder(AreteFilterDefinition definition)
    {
        return definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase)
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? "dd/mm/yyyy" : "yyyy-mm-dd")
            : "NULL";
    }

    public static string GetParameterInputMode(AreteFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "numeric",
            "int" => "numeric",
            "decimal" => "decimal",
            _ => "text"
        };
    }

    public static string GetParameterInputType(AreteFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "text",
            "datetime" => "datetime-local",
            "int" or "decimal" => "number",
            _ => "text"
        };
    }

    public static string GetParameterDescription(AreteFilterDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            return definition.Description;
        }

        return string.IsNullOrWhiteSpace(definition.OutputFormat)
            ? definition.DataType
            : $"{definition.DataType}, format={definition.OutputFormat}";
    }

    public static string GetFilterInputType(AreteFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "text",
            "datetime" => "text",
            "int" or "decimal" => "number",
            _ => "text"
        };
    }

    public static string GetFilterInputMode(AreteFilterDefinition definition)
    {
        return definition.DataType switch
        {
            "int" => "numeric",
            "decimal" => "decimal",
            _ => "text"
        };
    }

    public static string GetFilterInputLanguage(AreteFilterDefinition definition)
    {
        return definition.DataType is "date" or "datetime"
            ? "en-CA"
            : string.Empty;
    }

    public static string FormatFilterInputValue(AreteFilterDefinition definition, string value)
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
        Queries = areteQueryService.ListQueries(allowedCountries);
        if (string.IsNullOrWhiteSpace(QueryId))
        {
            var requestedArea = GetKnownArea(AreteArea);
            QueryId = (requestedArea is null
                    ? Queries.FirstOrDefault()
                    : Queries.FirstOrDefault(query =>
                        query.Area.Equals(requestedArea, StringComparison.OrdinalIgnoreCase)))
                ?.Id ?? string.Empty;
        }

        SelectedQuery = Queries.FirstOrDefault(query =>
            query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));
    }

    private string? GetKnownArea(string area)
    {
        return Pillars.FirstOrDefault(pillar =>
            pillar.Area.Equals(area, StringComparison.OrdinalIgnoreCase))?.Area;
    }

    private IActionResult DenyPage()
    {
        ErrorMessage = "Siam Windows vartotojui ARETE aplikacijos USER salies teise nesuteikta.";
        return Page();
    }

    private async Task<bool> CanOpenAsync()
    {
        try
        {
            var hasAccess = await areteAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
            allowedCountries = hasAccess
                ? await queryCountryAccessService.GetUserCountriesAsync(User, "ARETE", "DataExchangeSql", HttpContext.RequestAborted)
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

    private static string BuildCsvFileName(AreteQueryDefinition query)
    {
        return Path.ChangeExtension(BuildExcelFileName(query), ".csv")!;
    }

    private static string BuildExcelFileName(AreteQueryDefinition query)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var safeName = string.Concat(query.Name.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        return $"ARETE_{safeName}_{timestamp}.xlsx";
    }
}

public sealed record AreteRequiredFilterRow(
    AreteFilterDefinition Definition,
    string Operator,
    string Value);

public sealed record AretePillar(
    string Letter,
    string Area,
    string TitleKey,
    string BodyKey);













