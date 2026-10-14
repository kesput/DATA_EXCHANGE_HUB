using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace DATA_EXCHANGE_HUB.Pages.Apis;

public sealed class IndexModel(
    IApisQueryService apisQueryService,
    IApisAccessService apisAccessService,
    IApisQvdPreviewService apisQvdPreviewService,
    IQueryCountryAccessService queryCountryAccessService,
    IExcelWorkbookService excelWorkbookService,
    IMemoryCache memoryCache,
    IWebHostEnvironment webHostEnvironment) : PageModel
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const int ResultPageSize = 10000;
    private IReadOnlyDictionary<string, string>? pccPosMap;
    private IReadOnlySet<string> allowedCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)]
    public string QueryId { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Area { get; set; } = "Analytics";

    [BindProperty(SupportsGet = true)]
    public long? Refresh { get; set; }

    [BindProperty(SupportsGet = true)]
    public string QvdFileId { get; set; } = string.Empty;

    [BindProperty]
    public List<ApisParameterValue> Parameters { get; set; } = [];

    [BindProperty]
    public int ResultPage { get; set; } = 1;

    public IReadOnlyList<ApisQueryDefinition> Queries { get; private set; } = [];

    public ApisQueryDefinition? SelectedQuery { get; private set; }

    public ApisQueryResult? Result { get; private set; }

    public IReadOnlyList<ApisQvdRoot> QvdRoots { get; private set; } = [];

    public IReadOnlyList<ApisQvdFile> QvdFiles { get; private set; } = [];

    public ApisQvdPreview? QvdPreview { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool CanViewQvdPreview { get; private set; }

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

    public string ActiveArea => string.IsNullOrWhiteSpace(Area) ? SelectedQuery?.Area ?? "Analytics" : Area;

    public DateTime? ResultUpdatedAt { get; private set; }

    public bool CanRefreshResult { get; private set; } = true;

    public IReadOnlyList<ApisPillar> Pillars { get; } =
    [
        new("A", "Analytics", "Analize", "duomenu apdorojimas", "Analytics", "data analysis"),
        new("P", "Processes", "Procesai", "veiklos srautai", "Performance", "efficiency metrics"),
        new("I", "Indicators", "Indikatoriai", "pagrindiniai KPI", "Indicators", "key KPIs"),
        new("S", "Statistics", "Statistika", "rodikliu suvestine", "Statistics", "metrics summary")
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        var hasRequestedQuery = !string.IsNullOrWhiteSpace(Request.Query[nameof(QueryId)]);
        if (SelectedQuery is not null && ShouldExecuteOnGet(SelectedQuery, hasRequestedQuery))
        {
            try
            {
                var cachedResult = await GetOrRefreshResultAsync(
                    SelectedQuery,
                    Parameters,
                    forceRefresh: Refresh.HasValue,
                    HttpContext.RequestAborted);
                Result = cachedResult.Result;
                ResultUpdatedAt = cachedResult.UpdatedAt;
                CanRefreshResult = IsRefreshAllowed(cachedResult.UpdatedAt);
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostExecuteAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        if (SelectedQuery is null)
        {
            ErrorMessage = "APIS SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            Result = await apisQueryService.ExecuteAsync(
                SelectedQuery.Id,
                Parameters,
                allowedCountries,
                HttpContext.RequestAborted);
            CacheResult(SelectedQuery, Parameters, new CachedApisQueryResult(Result, DateTime.Now));
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResultPageAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        if (SelectedQuery is null)
        {
            ErrorMessage = "APIS SQL uzklausa nerasta.";
            return Page();
        }

        var cacheKey = BuildResultCacheKey(SelectedQuery, Parameters);
        if (!memoryCache.TryGetValue(cacheKey, out CachedApisQueryResult? cachedResult) || cachedResult is null)
        {
            ErrorMessage = "Rezultatas neberastas cache. Paleiskite EXECUTE dar karta.";
            return Page();
        }

        Result = cachedResult.Result;
        ResultUpdatedAt = cachedResult.UpdatedAt;
        CanRefreshResult = IsRefreshAllowed(cachedResult.UpdatedAt);
        return Page();
    }
    public async Task<IActionResult> OnPostExportExcelAsync()
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        if (SelectedQuery is null)
        {
            ErrorMessage = "APIS SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            var cachedResult = await GetOrRefreshResultAsync(
                SelectedQuery,
                Parameters,
                forceRefresh: false,
                HttpContext.RequestAborted);
            var result = cachedResult.Result;
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
        if (SelectedQuery is null)
        {
            ErrorMessage = "APIS SQL uzklausa nerasta.";
            return Page();
        }

        try
        {
            var cachedResult = await GetOrRefreshResultAsync(
                SelectedQuery,
                Parameters,
                forceRefresh: false,
                HttpContext.RequestAborted);
            var result = cachedResult.Result;
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

    public async Task<IActionResult> OnGetExportPccPickDelayCountryExcelAsync(string country)
    {
        if (!await CanOpenAsync())
        {
            return DenyPage();
        }

        LoadQueries();
        if (SelectedQuery is null)
        {
            return BadRequest("APIS SQL uzklausa nerasta.");
        }

        if (!SelectedQuery.Name.Equals("VN_NOT_PICKED_PCC_FLOWS", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Detalus exportas galimas tik VN_NOT_PICKED_PCC_FLOWS indikatoriui.");
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            return BadRequest("Nenurodyta salis.");
        }

        var result = await apisQueryService.ExecuteAsync(
            SelectedQuery.Id,
            Parameters,
            allowedCountries,
                HttpContext.RequestAborted);
        var dateKeys = Enumerable
            .Range(0, 14)
            .Select(offset => DateTime.Today.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = result.Rows
            .Where(row => GetKpiText(row, "COUNTRY", "UNKNOWN").Equals(country, StringComparison.OrdinalIgnoreCase))
            .Where(row => dateKeys.Contains(GetKpiDateKey(row, "DVAZNEW")))
            .ToArray();
        var headers = result.Columns
            .Where(IsPccPickDelayDetailColumn)
            .ToArray();
        var file = excelWorkbookService.CreateWorkbook(
            headers,
            rows,
            $"{NormalizeExcelName(country)}_DETAIL");

        return File(
            file,
            ExcelContentType,
            $"APIS_PCC_PICK_DELAY_{NormalizeExcelName(country)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }
    public IReadOnlyList<ApisQueryDefinition> GetQueriesForArea(string area)
    {
        return Queries
            .Where(query => query.Area.Equals(area, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public string GetParameterValue(ApisParameterDefinition definition)
    {
        var postedValue = Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase))?.Value;
        return postedValue ?? definition.DefaultValue ?? string.Empty;
    }

    public string GetParameterOperator(ApisParameterDefinition definition)
    {
        var postedOperator = Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase))?.Operator;
        return string.IsNullOrWhiteSpace(postedOperator)
            ? ExchangeTableFilter.EqualsOperator
            : postedOperator;
    }

    public string GetParameterConnector(ApisParameterDefinition definition)
    {
        var postedConnector = Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase))?.Connector;
        return postedConnector?.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase) == true
            ? ExchangeTableFilter.OrConnector
            : ExchangeTableFilter.AndConnector;
    }

    public static string GetOperatorLabel(string parameterOperator)
    {
        return parameterOperator switch
        {
            ExchangeTableFilter.EqualsOperator => "=",
            ExchangeTableFilter.NotEqualsOperator => "!=",
            ExchangeTableFilter.LikeOperator => "LIKE",
            ExchangeTableFilter.InOperator => "IN",
            ExchangeTableFilter.GreaterThanOperator => ">",
            ExchangeTableFilter.GreaterThanOrEqualOperator => ">=",
            ExchangeTableFilter.LessThanOperator => "<",
            ExchangeTableFilter.LessThanOrEqualOperator => "<=",
            _ => parameterOperator
        };
    }

    public static string GetParameterInputType(ApisParameterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "text",
            "datetime" => "datetime-local",
            "int" or "decimal" => "number",
            _ => "text"
        };
    }


    public static bool IsDateParameter(ApisParameterDefinition definition)
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

    public string GetParameterDisplayValue(ApisParameterDefinition definition)
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

    public string GetParameterNativeDateValue(ApisParameterDefinition definition)
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
    public static string? GetParameterPattern(ApisParameterDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Pattern))
        {
            return definition.Pattern;
        }

        return definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase)
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? @"\d{2}/\d{2}/\d{4}" : @"\d{4}-\d{2}-\d{2}")
            : null;
    }

    public static string? GetParameterTitle(ApisParameterDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.PatternMessage))
        {
            return definition.PatternMessage;
        }

        if (!definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var format = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "dd/MM/yyyy"
            : "yyyy-MM-dd";
        return $"Data turi buti formatu {format}.";
    }
    public static string? GetParameterLanguage(ApisParameterDefinition definition)
    {
        return definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase)
            ? "en-CA"
            : null;
    }
    public static string GetParameterPlaceholder(ApisParameterDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.DefaultValue))
        {
            return definition.DefaultValue;
        }

        return definition.DataType.Equals("date", StringComparison.OrdinalIgnoreCase)
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? "dd/mm/yyyy" : "yyyy-mm-dd")
            : "NULL";
    }
    public static string GetParameterInputMode(ApisParameterDefinition definition)
    {
        return definition.DataType switch
        {
            "date" => "numeric",
            "int" => "numeric",
            "decimal" => "decimal",
            _ => "text"
        };
    }

    public static long SumKpiLong(IEnumerable<IReadOnlyDictionary<string, string?>> rows, string column)
    {
        return rows.Sum(row =>
            row.TryGetValue(column, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number)
                ? number
                : 0L);
    }

    public static decimal SumKpiDecimal(IEnumerable<IReadOnlyDictionary<string, string?>> rows, string column)
    {
        return rows.Sum(row => ParseKpiDecimal(row.TryGetValue(column, out var value) ? value : null));
    }

    public static string FormatKpiDecimal(decimal value)
    {
        return value.ToString("N2", CultureInfo.CurrentCulture);
    }

    public static string GetWarehouseHourBucket(IReadOnlyDictionary<string, string?> row)
    {
        var todayOrder = row.TryGetValue("TODAY_ORDER", out var todayOrderValue) ? todayOrderValue : null;
        var sendtimeHour = row.TryGetValue("SENDTIME_HOUR", out var sendtimeHourValue) ? sendtimeHourValue : null;

        if (todayOrder?.Equals("EARLIER", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "<07:00";
        }

        TimeSpan sendtime;
        if (DateTime.TryParse(sendtimeHour, CultureInfo.CurrentCulture, DateTimeStyles.None, out var sendtimeDate) ||
            DateTime.TryParse(sendtimeHour, CultureInfo.InvariantCulture, DateTimeStyles.None, out sendtimeDate))
        {
            sendtime = sendtimeDate.TimeOfDay;
        }
        else if (!TimeSpan.TryParse(sendtimeHour, CultureInfo.InvariantCulture, out sendtime))
        {
            return "<07:00";
        }

        if (sendtime <= TimeSpan.FromHours(7))
        {
            return "<07:00";
        }

        if (sendtime > TimeSpan.FromHours(17))
        {
            return ">17:00";
        }

        return $"{sendtime.Hours:00}:00";
    }

    public static bool IsWarehouseHourBucket(IReadOnlyDictionary<string, string?> row, string bucket)
    {
        return GetWarehouseHourBucket(row).Equals(bucket, StringComparison.OrdinalIgnoreCase);
    }
    public static string GetKpiText(IReadOnlyDictionary<string, string?> row, string column, string fallback = "-")
    {
        return row.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    public string GetPccName(IReadOnlyDictionary<string, string?> row)
    {
        var posId = GetKpiText(row, "POSID", string.Empty);
        var normalizedPosId = NormalizePccPosId(posId);
        if (!string.IsNullOrWhiteSpace(normalizedPosId) && GetPccPosMap().TryGetValue(normalizedPosId, out var pccName))
        {
            return pccName;
        }

        var country = GetKpiText(row, "COUNTRY", string.Empty);
        var address = GetKpiText(row, "ADDRESS", string.Empty);
        var label = string.IsNullOrWhiteSpace(normalizedPosId) ? "Unmapped PCC" : $"Unmapped {normalizedPosId}";
        if (!string.IsNullOrWhiteSpace(country))
        {
            label = $"{country} {label}";
        }

        return string.IsNullOrWhiteSpace(address) ? label : $"{label} - {address}";
    }


    public static string GetKpiDateKey(IReadOnlyDictionary<string, string?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedValue = value.Trim();
        if (DateTime.TryParseExact(normalizedValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compactDate) ||
            DateTime.TryParse(normalizedValue, CultureInfo.CurrentCulture, DateTimeStyles.None, out compactDate) ||
            DateTime.TryParse(normalizedValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out compactDate))
        {
            return compactDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return normalizedValue.Length >= 10 ? normalizedValue[..10] : normalizedValue;
    }

    private static decimal ParseKpiDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0M;
        }

        var normalizedValue = value.Trim();
        return decimal.TryParse(normalizedValue, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentValue) ||
               decimal.TryParse(normalizedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out currentValue) ||
               decimal.TryParse(normalizedValue.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out currentValue)
            ? currentValue
            : 0M;
    }
    private IReadOnlyDictionary<string, string> GetPccPosMap()
    {
        if (pccPosMap is not null)
        {
            return pccPosMap;
        }

        var filePath = Path.Combine(webHostEnvironment.ContentRootPath, "Data", "Apis", "pcc-pos-map.json");
        if (!System.IO.File.Exists(filePath))
        {
            pccPosMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return pccPosMap;
        }

        var mapFile = System.Text.Json.JsonSerializer.Deserialize<PccPosMapFile>(
            System.IO.File.ReadAllText(filePath));
        pccPosMap = mapFile?.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.PosId) && !string.IsNullOrWhiteSpace(item.PccName))
            .GroupBy(item => NormalizePccPosId(item.PosId), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group.First().PccName.Trim(),
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return pccPosMap;
    }

    private static string NormalizePccPosId(string? posId)
    {
        if (string.IsNullOrWhiteSpace(posId))
        {
            return string.Empty;
        }

        var normalized = posId.Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentValue) ||
               decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out currentValue)
            ? decimal.Truncate(currentValue).ToString("0", CultureInfo.InvariantCulture)
            : normalized;
    }
    private static bool ShouldExecuteOnGet(ApisQueryDefinition? query, bool hasRequestedQuery)
    {
        if (query is null)
        {
            return false;
        }

        if (query.ViewMode.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return hasRequestedQuery &&
            query.Area.Equals("Indicators", StringComparison.OrdinalIgnoreCase) &&
            query.Parameters.All(parameter => !parameter.IsRequired);
    }
    private void LoadQvdPreview()
    {
        if (!CanViewQvdPreview)
        {
            QvdRoots = [];
            QvdFiles = [];
            QvdPreview = null;
            QvdFileId = string.Empty;
            return;
        }

        QvdRoots = apisQvdPreviewService.ListRoots();
        QvdFiles = apisQvdPreviewService.ListFiles();
        if (!ActiveArea.Equals("Analytics", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            QvdPreview = apisQvdPreviewService.ReadPreview(QvdFileId, 100);
            if (QvdPreview is not null && string.IsNullOrWhiteSpace(QvdFileId))
            {
                QvdFileId = QvdPreview.File.Id;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage)
                ? exception.Message
                : $"{ErrorMessage} {exception.Message}";
        }
    }
    private void LoadQueries()
    {
        Queries = apisQueryService.ListQueries(allowedCountries);
        var requestedArea = string.IsNullOrWhiteSpace(Area) ? "Analytics" : Area;
        var selectedById = string.IsNullOrWhiteSpace(QueryId)
            ? null
            : Queries.FirstOrDefault(query => query.Id.Equals(QueryId, StringComparison.OrdinalIgnoreCase));

        if (selectedById is not null && string.IsNullOrWhiteSpace(Request.Query[nameof(Area)]))
        {
            requestedArea = selectedById.Area;
        }

        if (!Pillars.Any(pillar => pillar.Area.Equals(requestedArea, StringComparison.OrdinalIgnoreCase)))
        {
            requestedArea = "Analytics";
        }

        Area = requestedArea;
        SelectedQuery = selectedById?.Area.Equals(Area, StringComparison.OrdinalIgnoreCase) == true
            ? selectedById
            : Queries.FirstOrDefault(query => query.Area.Equals(Area, StringComparison.OrdinalIgnoreCase));
        QueryId = SelectedQuery?.Id ?? string.Empty;
    }

    private IActionResult DenyPage()
    {
        ErrorMessage = "Siam Windows vartotojui APIS aplikacijos USER salies teise nesuteikta.";
        return Page();
    }

    private async Task<bool> CanOpenAsync()
    {
        try
        {
            var hasAccess = await apisAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
            if (!hasAccess)
            {
                CanViewQvdPreview = false;
                allowedCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return false;
            }

            CanViewQvdPreview = false;
            allowedCountries = await queryCountryAccessService.GetUserCountriesAsync(
                User,
                "APIS",
                "DataExchangeSql",
                HttpContext.RequestAborted);
            return hasAccess && allowedCountries.Count > 0;
        }
        catch
        {
            CanViewQvdPreview = false;
            return false;
        }
    }
    private async Task<CachedApisQueryResult> GetOrRefreshResultAsync(
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue> parameters,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildResultCacheKey(query, parameters);
        if (memoryCache.TryGetValue(cacheKey, out CachedApisQueryResult? cachedResult) &&
            cachedResult is not null &&
            !ShouldRefreshCachedResult(cachedResult.UpdatedAt, forceRefresh))
        {
            return cachedResult;
        }

        var result = await apisQueryService.ExecuteAsync(
            query.Id,
            parameters,
            allowedCountries,
            cancellationToken);
        var refreshedResult = new CachedApisQueryResult(result, DateTime.Now);
        CacheResult(query, parameters, refreshedResult);
        return refreshedResult;
    }

    private static bool ShouldRefreshCachedResult(DateTime updatedAt, bool forceRefresh)
    {
        var age = DateTime.Now - updatedAt;
        return forceRefresh ||
            age >= TimeSpan.FromMinutes(15);
    }

    private static bool IsRefreshAllowed(DateTime updatedAt)
    {
        return DateTime.Now - updatedAt >= TimeSpan.FromMinutes(5);
    }

    private void CacheResult(
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue> parameters,
        CachedApisQueryResult result)
    {
        memoryCache.Set(
            BuildResultCacheKey(query, parameters),
            result,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(20),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });
    }
    private static bool IsPccPickDelayDetailColumn(string column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return false;
        }

        if (column.Equals("ORDSTATUS_TMP", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !column.Contains("TIME", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExcelName(string value)
    {
        var safeName = string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        return string.IsNullOrWhiteSpace(safeName) ? "COUNTRY" : safeName;
    }
    private string BuildResultCacheKey(
        ApisQueryDefinition query,
        IReadOnlyList<ApisParameterValue> parameters)
    {
        var cacheOwner = IsSharedIndicatorQuery(query)
            ? "shared"
            : User.Identity?.Name ?? HttpContext.Session.Id;
        var normalizedParameters = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .Select(parameter => $"{parameter.Name.Trim()}:{parameter.Connector.Trim()}:{parameter.Operator.Trim()}={parameter.Value?.Trim() ?? string.Empty}");
        var sqlVersion = GetQueryFileVersion(query);
        var rawKey = string.Join("|", new[] { cacheOwner, query.Id, sqlVersion }.Concat(normalizedParameters));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
        return $"apis-result:{hash}";
    }

    private string GetQueryFileVersion(ApisQueryDefinition query)
    {
        var queryPath = Path.Combine(webHostEnvironment.ContentRootPath, query.RelativePath);
        return System.IO.File.Exists(queryPath)
            ? System.IO.File.GetLastWriteTimeUtc(queryPath).Ticks.ToString(CultureInfo.InvariantCulture)
            : "missing";
    }
    private static bool IsSharedIndicatorQuery(ApisQueryDefinition query)
    {
        return query.Area.Equals("Indicators", StringComparison.OrdinalIgnoreCase);
    }
    private void AddExportSummaryHeaders(int rowCount, int columnCount)
    {
        Response.Headers.Append("X-Export-Row-Count", rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Response.Headers.Append("X-Export-Column-Count", columnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string BuildCsvFileName(ApisQueryDefinition query)
    {
        return Path.ChangeExtension(BuildExcelFileName(query), ".csv")!;
    }

    private static string BuildExcelFileName(ApisQueryDefinition query)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var safeName = string.Concat(query.Name.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        return $"APIS_{safeName}_{timestamp}.xlsx";
    }
}

public sealed record PccPosMapFile(
    [property: JsonPropertyName("items")] IReadOnlyList<PccPosMapItem> Items);

public sealed record PccPosMapItem(
    [property: JsonPropertyName("posId")] string PosId,
    [property: JsonPropertyName("pccName")] string PccName);

public sealed record CachedApisQueryResult(
    ApisQueryResult Result,
    DateTime UpdatedAt);

public sealed record ApisPillar(
    string Letter,
    string Area,
    string TitleLt,
    string DescriptionLt,
    string TitleEn,
    string DescriptionEn);













