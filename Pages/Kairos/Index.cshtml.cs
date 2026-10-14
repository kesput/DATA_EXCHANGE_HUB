using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Kairos;

public sealed class IndexModel(
    IKairosService kairosService,
    IUserNameResolver userNameResolver,
    IQueryCountryAccessService queryCountryAccessService,
    IExcelWorkbookService excelWorkbookService,
    IDataExchangeTableService tableService,
    IKairosAnalysisQueryService kairosAnalysisQueryService) : PageModel
{
    private static readonly CultureInfo LithuanianCulture = CultureInfo.GetCultureInfo("lt-LT");
    private static readonly TimeSpan CutoffTime = new(11, 0, 0);
    private const string PromoBuyFromTodayQueryId = "Sql/Kairos/LT/DataExchangeSql/PromoAnalysis/PROMO_BY_BUY_FROM_TODAY.sql";
    private const string PromoFromTodayProductsQueryId = "Sql/Kairos/LT/DataExchangeSql/PromoAnalysis/PROMO_FROM_TODAY_PRODUCTS.sql";
    private const string PromoDailyQtyFlowQueryId = "Sql/Kairos/LT/DataExchangeSql/PromoAnalysis/PROMO_DAILY_QTY_FLOW.sql";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string RowLimitAll = "all";
    private const int DefaultPromoForecastRowLimit = 100;
    private IReadOnlySet<string> allowedCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private const int DefaultPromoAnalysisRowLimit = 100;
    private static readonly ExchangeTableAccess PromoForecastTableAccess = new(
        "BLS_LOGIT_STAT.dbo.KAIROS_PROMO_FORECAST",
        "KAIROS",
        ExchangePermission.UploadAndReceive);

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string InfPrek { get; set; } = string.Empty;

    [BindProperty]
    public string QuantityRequestText { get; set; } = "1";

    [BindProperty]
    public DateTime NeedDate { get; set; }

    [BindProperty]
    public DateTime MinExpirationRequest { get; set; }

    [BindProperty]
    public string NetworkClient { get; set; } = string.Empty;

    [BindProperty]
    public string Comment { get; set; } = string.Empty;

    [BindProperty]
    public IFormFile? PromoForecastFile { get; set; }
    [BindProperty(SupportsGet = true)]
    public List<string> FilterColumns { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> FilterOperators { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> FilterValues { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> FilterConnectors { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string PromoForecastRowLimit { get; set; } = DefaultPromoForecastRowLimit.ToString(CultureInfo.InvariantCulture);

    [BindProperty(SupportsGet = true)]
    public string PromoAnalysisRowLimit { get; set; } = DefaultPromoAnalysisRowLimit.ToString(CultureInfo.InvariantCulture);
    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }
    [TempData]
    public int? PromoDeletePreviewCount { get; set; }

    [TempData]
    public string? PromoDeletePreviewFilterKey { get; set; }

    public string UserName { get; private set; } = string.Empty;

    public bool HasAccess { get; private set; }

    public DateTime MinimumNeedDate { get; private set; }

    public IReadOnlyList<KairosRequest> Requests { get; private set; } = [];

    public IReadOnlyList<KairosLogEntry> Logs { get; private set; } = [];

    public IReadOnlyList<KairosPromoForecast> PromoForecastRows { get; private set; } = [];

    public KairosAnalysisQueryResult? PromoBuyFromTodayAnalysisResult { get; private set; }

    public KairosAnalysisQueryResult? PromoFromTodayProductsAnalysisResult { get; private set; }

    public KairosAnalysisQueryResult? PromoDailyQtyFlowAnalysisResult { get; private set; }

    public ExchangeTableData? PromoForecastTableData { get; private set; }

    public IReadOnlyList<ExchangeLogEntry> PromoForecastAuditLogs { get; private set; } = [];

    public IReadOnlyList<ExchangeTableFilter> ActiveFilters { get; private set; } = [];

    public IReadOnlyList<ExchangeTableFilter> PromoFilterRows =>
        ActiveFilters.Count > 0
            ? ActiveFilters
            : new[] { new ExchangeTableFilter(string.Empty, ExchangeTableFilter.EqualsOperator, string.Empty, ExchangeTableFilter.AndConnector) };

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    public string CurrentPromoDeleteFilterKey => BuildFilterKey(ActiveFilters);

    public bool HasMatchingPromoDeletePreview =>
        PromoDeletePreviewCount.HasValue &&
        !string.IsNullOrWhiteSpace(PromoDeletePreviewFilterKey) &&
        PromoDeletePreviewFilterKey.Equals(CurrentPromoDeleteFilterKey, StringComparison.Ordinal);

    public IReadOnlyList<IReadOnlyDictionary<string, string?>> PromoBuyFromTodayAnalysisRows =>
        ApplyRowLimit(PromoBuyFromTodayAnalysisResult?.Rows ?? [], PromoAnalysisRowLimitValue);

    public IReadOnlyList<IReadOnlyDictionary<string, string?>> PromoFromTodayProductsAnalysisRows =>
        ApplyRowLimit(PromoFromTodayProductsAnalysisResult?.Rows ?? [], PromoAnalysisRowLimitValue);

    public IReadOnlyList<IReadOnlyDictionary<string, string?>> PromoDailyQtyFlowAnalysisRows =>
        ApplyRowLimit(PromoDailyQtyFlowAnalysisResult?.Rows ?? [], PromoAnalysisRowLimitValue);
    public int PromoBuyFromTodayAnalysisTotalCount => PromoBuyFromTodayAnalysisResult?.Rows.Count ?? 0;

    public int PromoFromTodayProductsAnalysisTotalCount => PromoFromTodayProductsAnalysisResult?.Rows.Count ?? 0;

    public int PromoDailyQtyFlowAnalysisTotalCount => PromoDailyQtyFlowAnalysisResult?.Rows.Count ?? 0;

    public int PromoForecastRowLimitValue => ParseRowLimit(PromoForecastRowLimit, DefaultPromoForecastRowLimit);

    public int PromoAnalysisRowLimitValue => ParseRowLimit(PromoAnalysisRowLimit, DefaultPromoAnalysisRowLimit);

    public bool IsPromoForecastAllRows => IsAllRows(PromoForecastRowLimit);

    public bool IsPromoAnalysisAllRows => IsAllRows(PromoAnalysisRowLimit);
    public IReadOnlyList<ExchangeTableRow> PromoBuyFromTodayRows => PromoForecastTableData?.Rows
        .Where(row => TryGetPromoDate(row, "BUY_START", out var buyStart) && buyStart.Date >= DateTime.Today)
        .OrderBy(row => TryGetPromoDate(row, "BUY_START", out var buyStart) ? buyStart : DateTime.MaxValue)
        .ThenBy(row => PromoCellValue(row, "CLIENT"))
        .ThenBy(row => PromoCellValue(row, "INF_PREK"))
        .ToArray() ?? [];
    public int ActiveRequestCount => Requests.Count(request => !request.IsLocked);

    public int LockedRequestCount => Requests.Count(request => request.IsLocked);

    public string MinimumNeedDateInput => FormatDate(MinimumNeedDate);

    public string MinimumNeedDateLabel => FormatDate(MinimumNeedDate);

    public string CutoffRuleText => DateTime.Now.TimeOfDay < CutoffTime
        ? "Iki 11:00 poreikio data gali buti siandien."
        : "Po 11:00 nauji poreikiai leidziami tik nuo rytojaus.";

    public async Task OnGetAsync()
    {
        ApplyDefaultFormValues();
        await LoadPageDataAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return Page();
        }

        if (!TryBuildCreateRequest(out var createRequest))
        {
            return Page();
        }

        try
        {
            var requestId = await kairosService.CreateRequestAsync(
                createRequest,
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = $"KAIROS poreikis #{requestId} issaugotas.";
            return RedirectToPage();
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int requestId)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage();
        }

        try
        {
            await kairosService.DeleteRequestAsync(
                requestId,
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = $"KAIROS poreikis #{requestId} istrintas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(long logId)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage();
        }

        try
        {
            await kairosService.RestoreDeletedRequestAsync(
                logId,
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = $"KAIROS audit irasas #{logId} atstatytas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPage();
    }


    public IActionResult OnPostFilterPromoForecast()
    {
        ActiveFilters = BuildActiveFilters();
        return RedirectToPromoForecast(includeFilters: true);
    }

    public IActionResult OnPostClearPromoForecastFilters()
    {
        PromoDeletePreviewCount = null;
        PromoDeletePreviewFilterKey = null;
        return RedirectToPage(null, null, "kairos-promo-forecast");
    }

    public async Task<IActionResult> OnPostPreviewDeletePromoForecastFilteredAsync()
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPromoForecast(includeFilters: true);
        }

        if (!HasActiveFilters)
        {
            ErrorMessage = "Blokiniam trynimui privalomas bent vienas aktyvus filtras.";
            return RedirectToPromoForecast(includeFilters: true);
        }

        try
        {
            PromoDeletePreviewCount = await tableService.CountRowsByFilterAsync(
                PromoForecastTableAccess,
                ActiveFilters,
                HttpContext.RequestAborted);
            PromoDeletePreviewFilterKey = CurrentPromoDeleteFilterKey;
            SuccessMessage = $"Pagal filtrus rasta irasu: {PromoDeletePreviewCount}.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPromoForecast(includeFilters: true);
    }

    public async Task<IActionResult> OnPostDeletePromoForecastFilteredAsync(string? deletePreviewKey)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPromoForecast(includeFilters: true);
        }

        if (!HasActiveFilters)
        {
            ErrorMessage = "Blokiniam trynimui privalomas bent vienas aktyvus filtras.";
            return RedirectToPromoForecast(includeFilters: true);
        }

        if (string.IsNullOrWhiteSpace(deletePreviewKey) ||
            !deletePreviewKey.Equals(CurrentPromoDeleteFilterKey, StringComparison.Ordinal))
        {
            ErrorMessage = "Pirma atlikite blokinio trynimo perziura pagal tuos pacius filtrus.";
            return RedirectToPromoForecast(includeFilters: true);
        }

        try
        {
            var deletedRows = await tableService.DeleteRowsByFilterAsync(
                PromoForecastTableAccess,
                ActiveFilters,
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = $"Pagal filtrus istrinta irasu: {deletedRows}.";
            PromoDeletePreviewCount = null;
            PromoDeletePreviewFilterKey = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPromoForecast(includeFilters: true);
    }
    public async Task<IActionResult> OnPostDeletePromoForecastAsync(int id)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage(null, null, "kairos-promo-forecast");
        }

        try
        {
            await tableService.DeleteRowAsync(
                PromoForecastTableAccess,
                id.ToString(CultureInfo.InvariantCulture),
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = $"PROMO FORECAST irasas #{id} istrintas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPage(null, null, "kairos-promo-forecast");
    }
    public async Task<IActionResult> OnPostRevertPromoForecastLogAsync(long logId)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage(null, null, "kairos-promo-forecast");
        }

        try
        {
            await tableService.RevertLogEntryAsync(
                PromoForecastTableAccess,
                logId,
                UserName,
                canRevertAnyUser: true,
                HttpContext.RequestAborted);
            SuccessMessage = $"PROMO FORECAST audit irasas #{logId} atstatytas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPage(null, null, "kairos-promo-forecast");
    }

    public async Task<IActionResult> OnPostUpdatePromoForecastRowsAsync(
        [FromBody] ExchangeTableUpdateRequest request)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            return BadRequest("Siam Windows vartotojui KAIROS aplikacijos USER salies teise nesuteikta.");
        }

        try
        {
            await tableService.UpdateRowsAsync(
                PromoForecastTableAccess,
                request.Updates,
                UserName,
                HttpContext.RequestAborted);
            return new JsonResult(new { ok = true });
        }
        catch (Exception exception)
        {
            return BadRequest(FormatErrorMessage(exception));
        }
    }
    public async Task<IActionResult> OnPostImportPromoForecastAsync()
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage(null, null, "kairos-promo-forecast");
        }

        if (PromoForecastFile is null || PromoForecastFile.Length == 0)
        {
            ErrorMessage = "Pasirinkite PROMO FORECAST Excel faila.";
            return RedirectToPage(null, null, "kairos-promo-forecast");
        }

        if (!ExcelFileNames.IsSupportedUpload(PromoForecastFile.FileName))
        {
            ErrorMessage = "PROMO FORECAST importui naudokite .xlsx arba .xlsm faila.";
            return RedirectToPage(null, null, "kairos-promo-forecast");
        }

        try
        {
            await using var stream = PromoForecastFile.OpenReadStream();
            var worksheet = excelWorkbookService.ReadWorkbook(stream);
            var result = await kairosService.ImportPromoForecastAsync(
                worksheet,
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = result.IgnoredColumns == 0
                ? $"PROMO FORECAST issaugota eiluciu: {result.SavedRows}."
                : $"PROMO FORECAST issaugota eiluciu: {result.SavedRows}. Ignoruoti stulpeliai: {string.Join(", ", result.IgnoredHeaders)}.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
        }

        return RedirectToPage(null, null, "kairos-promo-forecast");
    }

    public async Task<IActionResult> OnGetExportPromoForecastExcelAsync(string? exportScope)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage(null, null, "kairos-promo-forecast");
        }

        try
        {
            var exportAllFilteredRows = IsExportAll(exportScope);
            var tableData = await tableService.GetTableDataAsync(
                PromoForecastTableAccess,
                exportAllFilteredRows ? int.MaxValue : PromoForecastRowLimitValue,
                pageNumber: 1,
                includeRows: true,
                filters: ActiveFilters,
                cancellationToken: HttpContext.RequestAborted);
            var headers = tableData.Columns
                .Where(column => IsPromoVisibleColumn(column.Name))
                .Select(column => column.Name)
                .ToArray();
            var rows = ToExcelRows(tableData, headers);
            var file = excelWorkbookService.CreateWorkbook(headers, rows, "Promo forecast");
            return File(file, ExcelContentType, BuildKairosExcelFileName(
                "promo_forecast",
                exportAllFilteredRows ? "filtered_all" : $"top{PromoForecastRowLimitValue}"));
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatErrorMessage(exception);
            return RedirectToPromoForecast(includeFilters: true);
        }
    }

    public async Task<IActionResult> OnGetExportPromoAnalysisExcelAsync(string query)
    {
        await LoadPageDataAsync();
        if (!HasAccess)
        {
            ErrorMessage = "Siam Windows vartotojui KAIROS aplikacijos teise nesuteikta.";
            return RedirectToPage(null, null, "kairos-promo-analysis");
        }

        var result = query.Equals("products", StringComparison.OrdinalIgnoreCase)
            ? PromoFromTodayProductsAnalysisResult
            : query.Equals("daily", StringComparison.OrdinalIgnoreCase)
                ? PromoDailyQtyFlowAnalysisResult
                : PromoBuyFromTodayAnalysisResult;
        if (result is null)
        {
            ErrorMessage = "PROMO ANALYSIS duomenu eksportui nerasta.";
            return RedirectToPage(null, null, "kairos-promo-analysis");
        }

        var rows = ApplyRowLimit(result.Rows, PromoAnalysisRowLimitValue);
        var exportName = query.Equals("products", StringComparison.OrdinalIgnoreCase)
            ? "promo_analysis_products"
            : query.Equals("daily", StringComparison.OrdinalIgnoreCase)
                ? "promo_analysis_daily_qty_flow"
                : "promo_analysis_buy_from_today";
        var file = excelWorkbookService.CreateWorkbook(result.Columns, rows, "Promo analysis");
        return File(file, ExcelContentType, BuildKairosExcelFileName(
            exportName,
            IsPromoAnalysisAllRows ? "all" : $"top{PromoAnalysisRowLimitValue}"));
    }
    public static string FormatDate(DateTime value)
    {
        return UiFormat.Date(value);
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return UiFormat.DateTimeMinute(value);
    }

    public static string FormatQuantity(decimal value)
    {
        return value.ToString("N3", LithuanianCulture);
    }

    public static string AnalysisCellValue(IReadOnlyDictionary<string, string?> row, string columnName)
    {
        return row.TryGetValue(columnName, out var value) ? value ?? string.Empty : string.Empty;
    }
    public string PromoCellValue(ExchangeTableRow row, string columnName)
    {
        return row.Values.TryGetValue(columnName, out var value) ? value : string.Empty;
    }

    public static bool TryGetPromoDate(ExchangeTableRow row, string columnName, out DateTime value)
    {
        value = default;
        if (!row.Values.TryGetValue(columnName, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        return DateTime.TryParse(rawValue, LithuanianCulture, DateTimeStyles.None, out value) ||
               DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
    public static bool IsPromoEditableColumn(string columnName)
    {
        return columnName is "BUY_START" or "QTY" or "INFO_DATE" or "STATUS" or "DAILY_SALES";
    }

    public static bool IsPromoVisibleColumn(string columnName)
    {
        return columnName is "ID" or "CLIENT" or "INF_PREK" or "BUY_START" or "PROMO_START" or "PROMO_END" or "QTY" or "INFO_DATE" or "STATUS" or "DAILY_SALES" or "UPDATE_TIME" or "UPDATED_BY";
    }
    private async Task LoadPageDataAsync()
    {
        UserName = userNameResolver.ResolveUserName(User);
        MinimumNeedDate = kairosService.GetMinimumNeedDate();

        try
        {
            HasAccess = await kairosService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            HasAccess = false;
            ErrorMessage ??= FormatErrorMessage(exception);
        }

        if (HasAccess)
        {
            allowedCountries = await queryCountryAccessService.GetUserCountriesAsync(User, "KAIROS", "DataExchangeSql", HttpContext.RequestAborted);
            HasAccess = allowedCountries.Count > 0;
        }

        if (!HasAccess)
        {
            Requests = [];
            Logs = [];
            PromoForecastRows = [];
            PromoForecastTableData = null;
            PromoForecastAuditLogs = [];
            PromoBuyFromTodayAnalysisResult = null;
            PromoFromTodayProductsAnalysisResult = null;
            PromoDailyQtyFlowAnalysisResult = null;
            return;
        }

        try
        {
            Requests = await kairosService.ListRequestsAsync(HttpContext.RequestAborted);
            Logs = await kairosService.ListLogsAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Requests = [];
            Logs = [];
            ErrorMessage ??= FormatErrorMessage(exception);
        }

        ActiveFilters = BuildActiveFilters();

        try
        {
            PromoForecastTableData = await tableService.GetTableDataAsync(
                PromoForecastTableAccess,
                rowLimit: PromoForecastRowLimitValue,
                pageNumber: 1,
                includeRows: true,
                filters: ActiveFilters,
                cancellationToken: HttpContext.RequestAborted);
            PromoForecastAuditLogs = await tableService.ListExchangeLogsAsync(
                PromoForecastTableAccess,
                UserName,
                canSeeAllLogs: true,
                rowLimit: 25,
                cancellationToken: HttpContext.RequestAborted);
            PromoBuyFromTodayAnalysisResult = await kairosAnalysisQueryService.ExecuteAsync(
                PromoBuyFromTodayQueryId,
                filters: null,
                countries: allowedCountries,
                cancellationToken: HttpContext.RequestAborted);
            PromoFromTodayProductsAnalysisResult = await kairosAnalysisQueryService.ExecuteAsync(
                PromoFromTodayProductsQueryId,
                filters: null,
                countries: allowedCountries,
                cancellationToken: HttpContext.RequestAborted);
            PromoDailyQtyFlowAnalysisResult = await kairosAnalysisQueryService.ExecuteAsync(
                PromoDailyQtyFlowQueryId,
                filters: null,
                countries: allowedCountries,
                cancellationToken: HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            PromoForecastTableData = null;
            PromoForecastAuditLogs = [];
            PromoBuyFromTodayAnalysisResult = null;
            PromoFromTodayProductsAnalysisResult = null;
            PromoDailyQtyFlowAnalysisResult = null;
            ErrorMessage ??= FormatErrorMessage(exception);
        }
    }

    private void ApplyDefaultFormValues()
    {
        var minimumNeedDate = kairosService.GetMinimumNeedDate();
        if (NeedDate == default || NeedDate.Date < minimumNeedDate.Date)
        {
            NeedDate = minimumNeedDate;
        }

        var defaultExpiration = DateTime.Today.AddDays(7);
        if (MinExpirationRequest == default || MinExpirationRequest.Date < NeedDate.Date)
        {
            MinExpirationRequest = defaultExpiration.Date < NeedDate.Date
                ? NeedDate.Date
                : defaultExpiration.Date;
        }

        if (string.IsNullOrWhiteSpace(QuantityRequestText))
        {
            QuantityRequestText = "1";
        }
    }

    private bool TryBuildCreateRequest(out KairosCreateRequest createRequest)
    {
        createRequest = new KairosCreateRequest(
            Title,
            InfPrek,
            0,
            NeedDate,
            MinExpirationRequest,
            NetworkClient,
            Comment);

        if (!TryParseQuantity(QuantityRequestText, out var quantity))
        {
            ErrorMessage = "Neteisingas kiekis. Galima naudoti kableli arba taska.";
            return false;
        }

        createRequest = createRequest with
        {
            QuantityRequest = quantity
        };
        return true;
    }

    private static bool TryParseQuantity(string text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Number, LithuanianCulture, out value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }


    private static int ParseRowLimit(string rowLimit, int defaultValue)
    {
        if (IsAllRows(rowLimit))
        {
            return int.MaxValue;
        }

        return int.TryParse(rowLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? Math.Min(value, 100000)
            : defaultValue;
    }

    private static bool IsAllRows(string rowLimit)
    {
        return rowLimit.Equals(RowLimitAll, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExportAll(string? exportScope)
    {
        return exportScope?.Equals("filteredAll", StringComparison.OrdinalIgnoreCase) == true ||
               exportScope?.Equals("all", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ApplyRowLimit(
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        int rowLimit)
    {
        return rowLimit == int.MaxValue
            ? rows
            : rows.Take(rowLimit).ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ToExcelRows(
        ExchangeTableData tableData,
        IReadOnlyList<string> headers)
    {
        return tableData.Rows
            .Select(row => (IReadOnlyDictionary<string, string?>)headers.ToDictionary(
                header => header,
                header => row.Values.TryGetValue(header, out var value) ? value : string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string BuildKairosExcelFileName(string prefix, string suffix)
    {
        return $"KAIROS_{prefix}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    }
    private IActionResult RedirectToPromoForecast(bool includeFilters)
    {
        return RedirectToPage(
            pageName: null,
            pageHandler: null,
            routeValues: BuildPromoRouteValues(includeFilters),
            fragment: "kairos-promo-forecast");
    }
    private IDictionary<string, string> BuildPromoRouteValues(bool includeFilters)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(PromoForecastRowLimit)] = PromoForecastRowLimit,
            [nameof(PromoAnalysisRowLimit)] = PromoAnalysisRowLimit
        };

        if (!includeFilters)
        {
            return values;
        }

        for (var index = 0; index < ActiveFilters.Count; index++)
        {
            var filter = ActiveFilters[index];
            values[$"FilterColumns[{index}]"] = filter.ColumnName;
            values[$"FilterOperators[{index}]"] = filter.Operator;
            values[$"FilterValues[{index}]"] = filter.Value;
            values[$"FilterConnectors[{index}]"] = filter.Connector;
        }

        return values;
    }

    private IReadOnlyList<ExchangeTableFilter> BuildActiveFilters()
    {
        var filterCount = new[] { FilterColumns.Count, FilterOperators.Count, FilterValues.Count }.Max();
        if (filterCount == 0)
        {
            return [];
        }

        var filters = new List<ExchangeTableFilter>();
        for (var index = 0; index < filterCount; index++)
        {
            var filter = new ExchangeTableFilter(
                GetIndexedValue(FilterColumns, index).Trim(),
                NormalizeFilterOperator(GetIndexedValue(FilterOperators, index)),
                GetIndexedValue(FilterValues, index).Trim(),
                index == 0 ? ExchangeTableFilter.AndConnector : NormalizeFilterConnector(GetIndexedValue(FilterConnectors, index)));
            if (filter.IsActive)
            {
                filters.Add(filter);
            }
        }

        return filters;
    }

    private static string BuildFilterKey(IReadOnlyList<ExchangeTableFilter> filters)
    {
        var normalized = string.Join("\n", filters
            .Where(filter => filter.IsActive)
            .Select(filter => $"{filter.Connector.Trim().ToLowerInvariant()}|{filter.ColumnName.Trim().ToLowerInvariant()}|{filter.Operator.Trim().ToLowerInvariant()}|{filter.Value.Trim()}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string GetIndexedValue(IReadOnlyList<string> values, int index)
    {
        return index < values.Count ? values[index] ?? string.Empty : string.Empty;
    }

    private static string NormalizeFilterOperator(string filterOperator)
    {
        return filterOperator.Equals(ExchangeTableFilter.NotEqualsOperator, StringComparison.OrdinalIgnoreCase) ||
               filterOperator.Equals("!=", StringComparison.OrdinalIgnoreCase) ||
               filterOperator.Equals("<>", StringComparison.OrdinalIgnoreCase)
            ? ExchangeTableFilter.NotEqualsOperator
            : filterOperator.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.LikeOperator
            : filterOperator.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.InOperator
            : filterOperator.Equals(ExchangeTableFilter.BetweenOperator, StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.BetweenOperator
            : filterOperator.Equals(ExchangeTableFilter.GreaterThanOperator, StringComparison.OrdinalIgnoreCase) || filterOperator.Equals(">", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.GreaterThanOperator
            : filterOperator.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) || filterOperator.Equals(">=", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.GreaterThanOrEqualOperator
            : filterOperator.Equals(ExchangeTableFilter.LessThanOperator, StringComparison.OrdinalIgnoreCase) || filterOperator.Equals("<", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.LessThanOperator
            : filterOperator.Equals(ExchangeTableFilter.LessThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) || filterOperator.Equals("<=", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.LessThanOrEqualOperator
            : ExchangeTableFilter.EqualsOperator;
    }

    private static string NormalizeFilterConnector(string connector)
    {
        return connector.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase)
            ? ExchangeTableFilter.OrConnector
            : ExchangeTableFilter.AndConnector;
    }
    private static string FormatErrorMessage(Exception exception)
    {
        if (exception.Message.Contains("BS_EXCHANGE_LOG", StringComparison.OrdinalIgnoreCase))
        {
            return $"Data Exchange audit SQL objektas dar nesukurtas arba nepilnas: {exception.Message}";
        }

        if (exception.Message.Contains("KAIROS_PROMO_FORECAST", StringComparison.OrdinalIgnoreCase))
        {
            return $"KAIROS PROMO FORECAST SQL lentele dar nesukurta arba nepilna: {exception.Message}";
        }

        return exception.Message.Contains("KAIROS_REQUESTS", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("KAIROS_REQUEST_LOG", StringComparison.OrdinalIgnoreCase)
            ? "KAIROS SQL objektai dar nesukurti. Paleiskite Database/DataExchangeHub.sql serveryje."
            : exception.Message;
    }
}





















