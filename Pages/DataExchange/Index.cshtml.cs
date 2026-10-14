using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DATA_EXCHANGE_HUB.Pages.DataExchange;

public sealed class IndexModel(
    IExchangeAccessService accessService,
    IDataExchangeTableService tableService,
    IExcelWorkbookService excelWorkbookService,
    IOptions<DataExchangeOptions> dataExchangeOptions,
    IWebHostEnvironment environment,
    IStringLocalizer<DATA_EXCHANGE_HUB.SharedResource> localizer) : PageModel
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const string DefaultExchangeAccessTableName = "BLS_LOGIT_STAT.dbo.BS_EXCHANGE_ACCESS";
    private const string ExportScopeTop = "top";
    private const string ExportScopeFilteredAll = "filteredAll";
    private static readonly Regex YearMonthRegex = new(@"^\d{6}$", RegexOptions.Compiled);
    private readonly DataExchangeOptions _dataExchangeOptions = dataExchangeOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [BindProperty(SupportsGet = true)]
    public string? TableName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string RowLimit { get; set; } = "10";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public List<string> FilterColumns { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> FilterOperators { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> FilterValues { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> FilterConnectors { get; set; } = [];

    [BindProperty]
    public string? ImportCorrectionType { get; set; }

    [BindProperty]
    public string? ImportCorrectionYearMonth { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public int? DeletePreviewCount { get; set; }

    [TempData]
    public string? DeletePreviewFilterKey { get; set; }

    [TempData]
    public string? ImportPreviewToken { get; set; }

    public UserAccessResult Access { get; private set; } =
        new("unknown", ExchangePermission.None, true);

    public IReadOnlyList<ExchangeTableAccess> Tables { get; private set; } = [];

    public ExchangeTableAccess? SelectedTable { get; private set; }

    public ExchangeTableData? TableData { get; private set; }

    public IReadOnlyList<ExchangeTableFilter> ActiveFilters { get; private set; } = [];

    public IReadOnlyList<ExchangeLogEntry> AuditLogs { get; private set; } = [];

    public ExchangeImportPreview? ImportPreview { get; private set; }

    public IReadOnlyList<ExchangeTableFilter> FilterRows =>
        ActiveFilters.Count > 0
            ? ActiveFilters
            : new[]
            {
                new ExchangeTableFilter(
                    string.Empty,
                    ExchangeTableFilter.EqualsOperator,
                    string.Empty,
                    ExchangeTableFilter.AndConnector)
            };

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    public int RowLimitValue => NormalizeRowLimit(RowLimit);

    public IReadOnlyList<int> RowLimitOptions => [10, 100, 1000];

    public IDictionary<string, string> CurrentFilterRouteValues => BuildRouteValues(includeFilters: true);

    public IDictionary<string, string> ClearFilterRouteValues => BuildRouteValues(includeFilters: false);

    public IDictionary<string, string> PreviousPageRouteValues =>
        BuildRouteValues(includeFilters: true, pageNumber: Math.Max(1, PageNumber - 1));

    public IDictionary<string, string> NextPageRouteValues =>
        BuildRouteValues(includeFilters: true, pageNumber: PageNumber + 1);

    public int ReceiveTableCount => Tables.Count(table => table.CanReceive);

    public int UploadTableCount => Tables.Count(table => table.CanUpload);

    public bool CanEditSelectedTable =>
        SelectedTable?.CanUpload == true && TableData?.CanUpdateRows == true;

    public bool CanInsertSelectedTable =>
        SelectedTable is { CanUpload: true } &&
        !SelectedTable.IsCorrectionTypeProfile;

    public bool CanImportSelectedTable =>
        SelectedTable?.CanUpload == true;

    public bool IsAdminUser => Tables.Any(table => table.IsAdmin);

    public bool IsSelectedExchangeAccessTable =>
        IsExchangeAccessTable(SelectedTable?.TableName);

    public bool CanDeleteSelectedTable =>
        SelectedTable?.CanReceive == true &&
        SelectedTable?.CanUpload == true;

    public bool CanDeleteSelectedTableRows =>
        CanDeleteSelectedTable &&
        TableData?.CanUpdateRows == true;

    public bool CanDeleteFilteredRows => CanDeleteSelectedTable && HasActiveFilters;

    public string CurrentDeleteFilterKey => BuildFilterKey(ActiveFilters);

    public bool HasMatchingDeletePreview =>
        DeletePreviewCount.HasValue &&
        !string.IsNullOrWhiteSpace(DeletePreviewFilterKey) &&
        DeletePreviewFilterKey.Equals(CurrentDeleteFilterKey, StringComparison.Ordinal);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAddRowAsync()
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanInsertSelectedTable)
        {
            ErrorMessage = SelectedTable.IsCorrectionTypeProfile
                ? localizer["DataExchange.Error.CorrectionInsertDisabled"]
                : localizer["DataExchange.Error.NoInsertPermission"];
            return RedirectToSelectedTable();
        }

        try
        {
            await tableService.AddRowAsync(
                SelectedTable,
                Request.Form,
                Access.UserName,
                HttpContext.RequestAborted);
            SuccessMessage = string.Format(localizer["DataExchange.Success.RowAdded"].Value, SelectedTable.TableName);
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = GetImportPreviewAccessDeniedMessage();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnGetExportExcelAsync(string? exportScope)
    {
        try
        {
            var exportData = await LoadExportDataAsync(exportScope);
            var headers = exportData.TableData.Columns.Select(column => column.Name).ToArray();
            var rows = ToExcelRows(exportData.TableData);
            var file = excelWorkbookService.CreateWorkbook(headers, rows, "Data");
            var fileName = BuildExportFileName(SelectedTable!.TableName, exportData.Suffix, "xlsx");

            AddExportSummaryHeaders(exportData.TableData.Rows.Count, headers.Length);
            return File(file, ExcelContentType, fileName);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return RedirectToSelectedTable();
        }
    }

    public async Task<IActionResult> OnGetExportCsvAsync(string? exportScope)
    {
        try
        {
            var exportData = await LoadExportDataAsync(exportScope);
            var headers = exportData.TableData.Columns.Select(column => column.Name).ToArray();
            var rows = ToExcelRows(exportData.TableData);
            var file = CreateSemicolonCsv(headers, rows);
            var fileName = BuildExportFileName(SelectedTable!.TableName, exportData.Suffix, "csv");

            AddExportSummaryHeaders(exportData.TableData.Rows.Count, headers.Length);
            return File(file, CsvContentType, fileName);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return RedirectToSelectedTable();
        }
    }
    public async Task<IActionResult> OnGetExportTemplateAsync()
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToSelectedTable();
        }

        if (!CanImportSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.ImportNoAccess"];
            return RedirectToSelectedTable();
        }

        try
        {
            var tableData = await tableService.GetTableDataAsync(
                SelectedTable,
                rowLimit: 10,
                pageNumber: 1,
                includeRows: false,
                cancellationToken: HttpContext.RequestAborted);

            var headers = tableData.InsertableColumns.Select(column => column.Name).ToArray();
            if (headers.Length == 0)
            {
                ErrorMessage = localizer["DataExchange.Error.NoImportColumns"];
                return RedirectToSelectedTable();
            }

            var file = excelWorkbookService.CreateWorkbook(headers, [], "Template");
            var fileName = BuildExportFileName(SelectedTable.TableName, "template", "xlsx");

            return File(file, ExcelContentType, fileName);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            return RedirectToSelectedTable();
        }
    }

    public async Task<IActionResult> OnPostImportExcelAsync(IFormFile? excelFile)
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanImportSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.ImportNoAccess"];
            return RedirectToSelectedTable();
        }

        var correctionContextError = ValidateCorrectionImportContext(
            SelectedTable,
            ImportCorrectionType,
            ImportCorrectionYearMonth);
        if (!string.IsNullOrWhiteSpace(correctionContextError))
        {
            ErrorMessage = correctionContextError;
            return RedirectToSelectedTable();
        }

        if (excelFile is null || excelFile.Length == 0)
        {
            ErrorMessage = localizer["DataExchange.Error.SelectExcelFile"];
            return RedirectToSelectedTable();
        }

        if (!ExcelFileNames.IsSupportedUpload(excelFile.FileName))
        {
            ErrorMessage = localizer["DataExchange.Error.InvalidExcelFile"];
            return RedirectToSelectedTable();
        }

        try
        {
            var tableData = await tableService.GetTableDataAsync(
                SelectedTable,
                rowLimit: 10,
                pageNumber: 1,
                includeRows: false,
                cancellationToken: HttpContext.RequestAborted);
            if (tableData.InsertableColumns.Count == 0)
            {
                ErrorMessage = localizer["DataExchange.Error.NoImportColumns"];
                return RedirectToSelectedTable();
            }

            using var stream = excelFile.OpenReadStream();
            var worksheetData = NormalizeWorksheetDataForBehavior(
                excelWorkbookService.ReadWorkbook(stream),
                SelectedTable,
                ImportCorrectionType,
                ImportCorrectionYearMonth);
            var validationError = ValidateExcelHeaders(worksheetData.Headers, tableData.Columns);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                ErrorMessage = validationError;
                return RedirectToSelectedTable();
            }

            if (worksheetData.Rows.Count == 0)
            {
                ErrorMessage = localizer["DataExchange.Error.EmptyExcelFile"];
                return RedirectToSelectedTable();
            }

            var token = await SaveImportPreviewAsync(
                SelectedTable.TableName,
                excelFile.FileName,
                worksheetData.Headers,
                worksheetData.Rows,
                Access.UserName,
                HttpContext.RequestAborted);

            ImportPreviewToken = token;
            var ignoredHeaders = worksheetData.Headers
                .Where(ExchangeTableColumn.IsSystemManagedColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            SuccessMessage = ignoredHeaders.Length == 0
                ? string.Format(localizer["DataExchange.Success.ImportPreviewPrepared"].Value, worksheetData.Rows.Count)
                : string.Format(localizer["DataExchange.Success.ImportPreviewPreparedIgnored"].Value, worksheetData.Rows.Count, string.Join(", ", ignoredHeaders));
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostConfirmImportExcelAsync(string? importToken)
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanImportSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.ImportNoAccess"];
            return RedirectToSelectedTable();
        }

        var preview = await LoadImportPreviewAsync(importToken, HttpContext.RequestAborted);
        if (preview is null)
        {
            ErrorMessage = localizer["DataExchange.Error.PreviewMissing"];
            return RedirectToSelectedTable();
        }

        if (!preview.TableName.Equals(SelectedTable.TableName, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = localizer["DataExchange.Error.PreviewTableMismatch"];
            return RedirectToSelectedTable();
        }

        try
        {
            var rows = preview.Rows
                .Select(row => (IReadOnlyDictionary<string, string?>)row)
                .ToArray();
            var insertedRows = await tableService.AddRowsAsync(
                SelectedTable,
                rows,
                Access.UserName,
                HttpContext.RequestAborted);

            DeleteImportPreview(preview.Token);

            if (insertedRows == 0)
            {
                ErrorMessage = localizer["DataExchange.Error.EmptyImportRows"];
                return RedirectToSelectedTable();
            }

            SuccessMessage = insertedRows == 1
                ? string.Format(localizer["DataExchange.Success.ImportRowInserted"].Value, SelectedTable.TableName)
                : string.Format(localizer["DataExchange.Success.ImportRowsInserted"].Value, insertedRows, SelectedTable.TableName);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            ImportPreviewToken = preview.Token;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostDeleteRowAsync(string? keyValue)
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanDeleteSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.DeleteAccessDenied"];
            return RedirectToSelectedTable();
        }

        if (string.IsNullOrWhiteSpace(keyValue))
        {
            ErrorMessage = localizer["DataExchange.Error.DeleteRowKeyMissing"];
            return RedirectToSelectedTable();
        }

        try
        {
            await tableService.DeleteRowAsync(
                SelectedTable,
                keyValue,
                Access.UserName,
                HttpContext.RequestAborted);

            SuccessMessage = localizer["DataExchange.Success.RowDeleted"];
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostPreviewDeleteFilteredRowsAsync()
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanDeleteSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.DeleteAccessDenied"];
            return RedirectToSelectedTable();
        }

        if (!HasActiveFilters)
        {
            ErrorMessage = localizer["DataExchange.Error.DeletePreviewRequiresFilter"];
            return RedirectToSelectedTable();
        }

        try
        {
            DeletePreviewCount = await tableService.CountRowsByFilterAsync(
                SelectedTable,
                ActiveFilters,
                HttpContext.RequestAborted);
            DeletePreviewFilterKey = CurrentDeleteFilterKey;
            SuccessMessage = string.Format(localizer["DataExchange.Success.DeletePreviewFound"].Value, DeletePreviewCount);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostDeleteFilteredRowsAsync(string? deletePreviewKey)
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanDeleteSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.DeleteAccessDenied"];
            return RedirectToSelectedTable();
        }

        if (!HasActiveFilters)
        {
            ErrorMessage = localizer["DataExchange.Error.DeleteRequiresFilter"];
            return RedirectToSelectedTable();
        }

        if (string.IsNullOrWhiteSpace(deletePreviewKey) ||
            !deletePreviewKey.Equals(CurrentDeleteFilterKey, StringComparison.Ordinal))
        {
            ErrorMessage = localizer["DataExchange.Error.DeletePreviewRequired"];
            return RedirectToSelectedTable();
        }

        try
        {
            var deletedRows = await tableService.DeleteRowsByFilterAsync(
                SelectedTable,
                ActiveFilters,
                Access.UserName,
                HttpContext.RequestAborted);

            SuccessMessage = deletedRows == 1
                ? localizer["DataExchange.Success.FilteredRowDeleted"]
                : string.Format(localizer["DataExchange.Success.FilteredRowsDeleted"].Value, deletedRows);
            DeletePreviewCount = null;
            DeletePreviewFilterKey = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostRevertLogAsync(long logId)
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["DataExchange.Error.TableNotAssigned"];
            return RedirectToPage();
        }

        if (!CanDeleteSelectedTable)
        {
            ErrorMessage = localizer["DataExchange.Error.RevertAccessDenied"];
            return RedirectToSelectedTable();
        }

        try
        {
            await tableService.RevertLogEntryAsync(
                SelectedTable,
                logId,
                Access.UserName,
                IsAdminUser,
                HttpContext.RequestAborted);
            SuccessMessage = string.Format(localizer["DataExchange.Success.AuditReverted"].Value, logId);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostUpdateRowsAsync(
        [FromBody] ExchangeTableUpdateRequest request)
    {
        await LoadAccessAsync();
        TableName = request.TableName;
        SelectTable();

        if (SelectedTable is null)
        {
            return BadRequest(localizer["DataExchange.Error.TableNotAssigned"].Value);
        }

        if (!SelectedTable.CanUpload)
        {
            return Forbid();
        }

        try
        {
            await tableService.UpdateRowsAsync(
                SelectedTable,
                request.Updates,
                Access.UserName,
                HttpContext.RequestAborted);

            return new JsonResult(new { ok = true });
        }
        catch (Exception exception)
        {
            return BadRequest(exception.Message);
        }
    }

    public string CellValue(ExchangeTableRow row, string columnName)
    {
        return row.Values.TryGetValue(columnName, out var value) ? value : string.Empty;
    }

    public bool IsBooleanColumn(ExchangeTableColumn column)
    {
        return column.DataType.Equals("bit", StringComparison.OrdinalIgnoreCase);
    }

    public string InputTypeForColumn(ExchangeTableColumn column)
    {
        var dataType = column.DataType.ToLowerInvariant();
        if (dataType.Contains("date", StringComparison.Ordinal) ||
            dataType.Contains("time", StringComparison.Ordinal))
        {
            return dataType.Equals("date", StringComparison.Ordinal) ? "date" : "datetime-local";
        }

        if (IsNumericColumn(column))
        {
            return "number";
        }

        return "text";
    }

    public string InputStepForColumn(ExchangeTableColumn column)
    {
        if (IsDecimalColumn(column))
        {
            return "any";
        }

        return IsNumericColumn(column) ? "1" : string.Empty;
    }

    public string InputPlaceholderForColumn(ExchangeTableColumn column)
    {
        if (IsBooleanColumn(column))
        {
            return localizer["Themis.Placeholder.Boolean"];
        }

        var inputType = InputTypeForColumn(column);
        return inputType switch
        {
            "date" => "yyyy-mm-dd",
            "datetime-local" => "yyyy-mm-dd hh:mm",
            "number" => localizer["Themis.Placeholder.Number"].Value,
            _ => string.Empty
        };
    }

    private async Task LoadAsync()
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is not null)
        {
            try
            {
                TableData = await tableService.GetTableDataAsync(
                    SelectedTable,
                    RowLimitValue,
                    PageNumber,
                    includeRows: SelectedTable.CanReceive,
                    filters: ActiveFilters,
                    cancellationToken: HttpContext.RequestAborted);

                if (SelectedTable.CanReceive)
                {
                    AuditLogs = await tableService.ListExchangeLogsAsync(
                        SelectedTable,
                        Access.UserName,
                        IsAdminUser,
                        rowLimit: 25,
                        cancellationToken: HttpContext.RequestAborted);
                }

                ImportPreview = await LoadImportPreviewAsync(
                    ImportPreviewToken,
                    HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
            }
        }
    }

    private async Task LoadAccessAsync()
    {
        var accessDetails = await accessService.GetCurrentUserAccessDetailsAsync(
            User,
            HttpContext.RequestAborted);

        Access = accessDetails.UserAccess;
        Tables = accessDetails.Tables;
    }

    private void SelectTable()
    {
        SelectedTable = !string.IsNullOrWhiteSpace(TableName)
            ? Tables.FirstOrDefault(table =>
                table.TableName.Equals(TableName, StringComparison.OrdinalIgnoreCase))
            : Tables.FirstOrDefault(table => table.CanReceive) ?? Tables.FirstOrDefault();

        TableName = SelectedTable?.TableName;
        RowLimit = RowLimitValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PageNumber = Math.Max(1, PageNumber);
        ActiveFilters = BuildActiveFilters();
    }

    private IActionResult RedirectToSelectedTable()
    {
        return RedirectToPage(pageName: null, pageHandler: null, routeValues: BuildRouteValues(includeFilters: true));
    }

    private IReadOnlyList<ExchangeTableFilter> BuildActiveFilters()
    {
        var filterCount = new[]
        {
            FilterColumns.Count,
            FilterOperators.Count,
            FilterValues.Count
        }.Max();

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
                index == 0
                    ? ExchangeTableFilter.AndConnector
                    : NormalizeFilterConnector(GetIndexedValue(FilterConnectors, index)));

            if (filter.IsActive)
            {
                filters.Add(filter);
            }
        }

        return filters;
    }

    private IDictionary<string, string> BuildRouteValues(bool includeFilters, int? pageNumber = null)
    {
        var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rowLimit"] = RowLimitValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pageNumber"] = Math.Max(1, pageNumber ?? PageNumber).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var tableName = SelectedTable?.TableName ?? TableName;
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            routeValues["tableName"] = tableName;
        }

        if (!includeFilters)
        {
            return routeValues;
        }

        for (var index = 0; index < ActiveFilters.Count; index++)
        {
            var filter = ActiveFilters[index];
            routeValues[$"FilterColumns[{index}]"] = filter.ColumnName;
            routeValues[$"FilterOperators[{index}]"] = filter.Operator;
            routeValues[$"FilterValues[{index}]"] = filter.Value;
            routeValues[$"FilterConnectors[{index}]"] = filter.Connector;
        }

        return routeValues;
    }

    private static string GetIndexedValue(IReadOnlyList<string> values, int index)
    {
        return index < values.Count
            ? values[index] ?? string.Empty
            : string.Empty;
    }

    private static int NormalizeRowLimit(string rowLimit)
    {
        return int.TryParse(rowLimit, out var parsed) && parsed is 10 or 100 or 1000
            ? parsed
            : 10;
    }

    private static string BuildFilterKey(IReadOnlyList<ExchangeTableFilter> filters)
    {
        var normalized = string.Join(
            "\n",
            filters
                .Where(filter => filter.IsActive)
                .Select(filter =>
                    $"{filter.Connector.Trim().ToLowerInvariant()}|{filter.ColumnName.Trim().ToLowerInvariant()}|{filter.Operator.Trim().ToLowerInvariant()}|{filter.Value.Trim()}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static bool IsNumericColumn(ExchangeTableColumn column)
    {
        return column.DataType.Equals("tinyint", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("smallint", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("int", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("bigint", StringComparison.OrdinalIgnoreCase) ||
               IsDecimalColumn(column);
    }

    private static bool IsDecimalColumn(ExchangeTableColumn column)
    {
        return column.DataType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("numeric", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("money", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("smallmoney", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("float", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Equals("real", StringComparison.OrdinalIgnoreCase);
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
            : filterOperator.Equals(ExchangeTableFilter.GreaterThanOperator, StringComparison.OrdinalIgnoreCase) ||
              filterOperator.Equals(">", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.GreaterThanOperator
            : filterOperator.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) ||
              filterOperator.Equals(">=", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.GreaterThanOrEqualOperator
            : filterOperator.Equals(ExchangeTableFilter.LessThanOperator, StringComparison.OrdinalIgnoreCase) ||
              filterOperator.Equals("<", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.LessThanOperator
            : filterOperator.Equals(ExchangeTableFilter.LessThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) ||
              filterOperator.Equals("<=", StringComparison.OrdinalIgnoreCase)
                ? ExchangeTableFilter.LessThanOrEqualOperator
            : ExchangeTableFilter.EqualsOperator;
    }

    private static string NormalizeFilterConnector(string connector)
    {
        return connector.Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase)
            ? ExchangeTableFilter.OrConnector
            : ExchangeTableFilter.AndConnector;
    }

    private async Task<string> SaveImportPreviewAsync(
        string tableName,
        string fileName,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string userName,
        CancellationToken cancellationToken)
    {
        CleanupOldImportPreviews();

        var token = Guid.NewGuid().ToString("N");
        var preview = new ExchangeImportPreview(
            token,
            tableName,
            Path.GetFileName(fileName),
            userName,
            DateTimeOffset.Now,
            headers.ToList(),
            headers
                .Where(ExchangeTableColumn.IsSystemManagedColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            rows
                .Select(row => row.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase))
                .ToList());

        Directory.CreateDirectory(GetImportPreviewDirectory());
        await using var stream = System.IO.File.Create(GetImportPreviewPath(token));
        await JsonSerializer.SerializeAsync(stream, preview, JsonOptions, cancellationToken);
        return token;
    }

    private async Task<ExchangeImportPreview?> LoadImportPreviewAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        var path = GetImportPreviewPath(token);
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        await using var stream = System.IO.File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ExchangeImportPreview>(stream, JsonOptions, cancellationToken);
    }

    private void DeleteImportPreview(string token)
    {
        var path = GetImportPreviewPath(token);
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }
    }

    private void CleanupOldImportPreviews()
    {
        var directory = GetImportPreviewDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-12))
            {
                fileInfo.Delete();
            }
        }
    }

    private string GetImportPreviewDirectory()
    {
        var storagePath = _dataExchangeOptions.StoragePath;
        var rootPath = Path.IsPathRooted(storagePath)
            ? storagePath
            : Path.Combine(environment.ContentRootPath, storagePath);

        return Path.Combine(rootPath, "ImportPreviews");
    }

    private string GetImportPreviewAccessDeniedMessage()
    {
        return string.Format(
            localizer["DataExchange.Error.ImportAccessDenied"].Value,
            GetImportPreviewDirectory());
    }

    private string GetImportPreviewPath(string token)
    {
        return Path.Combine(GetImportPreviewDirectory(), $"{token}.json");
    }

    private async Task<(ExchangeTableData TableData, string Suffix)> LoadExportDataAsync(string? exportScope)
    {
        await LoadAccessAsync();
        SelectTable();

        if (SelectedTable is null)
        {
            throw new InvalidOperationException(localizer["DataExchange.Error.TableNotAssigned"]);
        }

        if (!SelectedTable.CanReceive)
        {
            throw new InvalidOperationException(localizer["DataExchange.Error.ExportNoReceive"]);
        }

        var exportAllFilteredRows = exportScope?.Equals(
            ExportScopeFilteredAll,
            StringComparison.OrdinalIgnoreCase) == true;
        var tableData = await tableService.GetTableDataAsync(
            SelectedTable,
            exportAllFilteredRows ? int.MaxValue : RowLimitValue,
            exportAllFilteredRows ? 1 : PageNumber,
            includeRows: true,
            filters: ActiveFilters,
            cancellationToken: HttpContext.RequestAborted);
        var suffix = exportAllFilteredRows
            ? "filtered_all"
            : $"top{RowLimitValue}_p{PageNumber}";

        return (tableData, suffix);
    }

    private void AddExportSummaryHeaders(int rowCount, int columnCount)
    {
        Response.Headers.Append("X-Export-Row-Count", rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Response.Headers.Append("X-Export-Column-Count", columnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static byte[] CreateSemicolonCsv(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("Eksportui nerasta stulpeliu.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("sep=;");
        builder.AppendLine(string.Join(';', headers.Select(EscapeCsvValue)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', headers.Select(header =>
                row.TryGetValue(header, out var value) ? EscapeCsvValue(value) : string.Empty)));
        }

        return Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(builder.ToString()))
            .ToArray();
    }

    private static string EscapeCsvValue(string? value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(';', StringComparison.Ordinal) &&
            !text.Contains('"', StringComparison.Ordinal) &&
            !text.Contains('\r', StringComparison.Ordinal) &&
            !text.Contains('\n', StringComparison.Ordinal))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ToExcelRows(
        ExchangeTableData tableData)
    {
        return tableData.Rows
            .Select(row => (IReadOnlyDictionary<string, string?>)tableData.Columns.ToDictionary(
                column => column.Name,
                column => row.Values.TryGetValue(column.Name, out var value)
                    ? value
                    : string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private string? ValidateExcelHeaders(
        IReadOnlyList<string> headers,
        IReadOnlyList<ExchangeTableColumn> columns)
    {
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            return localizer["DataExchange.Excel.EmptyHeader"];
        }

        var duplicates = headers
            .Where(header => !ExchangeTableColumn.IsSystemManagedColumnName(header))
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            return string.Format(localizer["DataExchange.Excel.DuplicateHeaders"].Value, string.Join(", ", duplicates));
        }

        var expectedHeaders = columns
            .Where(column => column.CanInsert)
            .Select(column => column.Name)
            .ToArray();
        var knownHeaders = columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualHeaders = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedHeaderSet = expectedHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingHeaders = expectedHeaders
            .Where(header => !actualHeaders.Contains(header))
            .ToArray();
        var extraHeaders = headers
            .Where(header =>
                !expectedHeaderSet.Contains(header) &&
                !knownHeaders.Contains(header) &&
                !ExchangeTableColumn.IsSystemManagedColumnName(header))
            .ToArray();

        if (missingHeaders.Length == 0 && extraHeaders.Length == 0)
        {
            return null;
        }

        var details = new List<string>();
        if (missingHeaders.Length > 0)
        {
            details.Add(string.Format(localizer["DataExchange.Excel.MissingHeaders"].Value, string.Join(", ", missingHeaders)));
        }

        if (extraHeaders.Length > 0)
        {
            details.Add(string.Format(localizer["DataExchange.Excel.ExtraHeaders"].Value, string.Join(", ", extraHeaders)));
        }

        return string.Format(localizer["DataExchange.Excel.StructureMismatch"].Value, string.Join("; ", details));
    }

    private static ExcelWorksheetData NormalizeWorksheetDataForBehavior(
        ExcelWorksheetData worksheetData,
        ExchangeTableAccess tableAccess,
        string? correctionType,
        string? correctionYearMonth)
    {
        if (!tableAccess.IsCorrectionTypeProfile)
        {
            return worksheetData;
        }

        var headers = worksheetData.Headers
            .Select(MapCorrectionTypeExcelHeader)
            .Append("CORRECTION_TYPE")
            .Append("CORRECTION_YEARMONTH")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedCorrectionType = NormalizeCorrectionType(correctionType);
        var normalizedCorrectionYearMonth = correctionYearMonth?.Trim();
        var rows = worksheetData.Rows
            .Select(row =>
            {
                var normalizedRow = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in row)
                {
                    normalizedRow[MapCorrectionTypeExcelHeader(cell.Key)] = cell.Value;
                }

                normalizedRow["CORRECTION_TYPE"] = normalizedCorrectionType;
                normalizedRow["CORRECTION_YEARMONTH"] = normalizedCorrectionYearMonth;

                return (IReadOnlyDictionary<string, string?>)normalizedRow;
            })
            .ToArray();

        return new ExcelWorksheetData(headers, rows);
    }

    private string? ValidateCorrectionImportContext(
        ExchangeTableAccess tableAccess,
        string? correctionType,
        string? correctionYearMonth)
    {
        if (!tableAccess.IsCorrectionTypeProfile)
        {
            return null;
        }

        var normalizedCorrectionType = NormalizeCorrectionType(correctionType);
        var normalizedCorrectionYearMonth = correctionYearMonth?.Trim() ?? string.Empty;
        var hasValidType =
            normalizedCorrectionType is "DISCOUNT" or "COMPENSATION";
        var hasValidYearMonth =
            YearMonthRegex.IsMatch(normalizedCorrectionYearMonth) &&
            int.TryParse(normalizedCorrectionYearMonth[4..6], out var month) &&
            month is >= 1 and <= 12;

        if (hasValidType && hasValidYearMonth)
        {
            return null;
        }

        return localizer["DataExchange.CorrectionContextRequired"];
    }

    private static string NormalizeCorrectionType(string? correctionType)
    {
        return correctionType?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    private static string MapCorrectionTypeExcelHeader(string header)
    {
        return header.Equals("ID", StringComparison.OrdinalIgnoreCase)
            ? "ID_TRP"
            : header;
    }

    private static string BuildExportFileName(string tableName, string suffix, string extension)
    {
        var fileName = tableName
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal);

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidCharacter, '_');
        }

        var normalizedExtension = extension.TrimStart('.');

        return $"{fileName}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}.{normalizedExtension}";
    }

    private bool IsExchangeAccessTable(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return false;
        }

        var configuredAccessTable = SqlName.NormalizeTableName(
            _dataExchangeOptions.ExchangeAccessTableName,
            DefaultExchangeAccessTableName);

        return SqlName.NormalizeTableName(tableName, DefaultExchangeAccessTableName)
            .Equals(configuredAccessTable, StringComparison.OrdinalIgnoreCase);
    }
}









