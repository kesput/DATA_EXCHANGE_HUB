using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DATA_EXCHANGE_HUB.Pages.Themis;

public sealed class IndexModel(
    IThemisService themisService,
    IUserNameResolver userNameResolver,
    IExcelWorkbookService excelWorkbookService,
    IWebHostEnvironment environment,
    IStringLocalizer<DATA_EXCHANGE_HUB.SharedResource> localizer) : PageModel
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv; charset=utf-8";

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

    public bool HasAccess { get; private set; }

    public string CurrentUserName { get; private set; } = string.Empty;

    public IReadOnlyList<ExchangeTableAccess> Tables { get; private set; } = [];

    public ExchangeTableAccess? SelectedTable { get; private set; }

    public ExchangeTableData? TableData { get; private set; }

    public IReadOnlyList<ExchangeTableFilter> ActiveFilters { get; private set; } = [];

    public IReadOnlyList<ExchangeLogEntry> AuditLogs { get; private set; } = [];

    public ExchangeImportPreview? ImportPreview { get; private set; }

    public IReadOnlyList<ExchangeTableFilter> FilterRows =>
        ActiveFilters.Count > 0
            ? ActiveFilters
            : [new ExchangeTableFilter(string.Empty, ExchangeTableFilter.EqualsOperator, string.Empty, ExchangeTableFilter.AndConnector)];

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    public int RowLimitValue => NormalizeRowLimit(RowLimit);

    public IReadOnlyList<int> RowLimitOptions => [10, 100, 1000];

    public bool CanEditSelectedTable => SelectedTable is not null && TableData?.CanUpdateRows == true;

    public bool CanDeleteFilteredRows => SelectedTable is not null && HasActiveFilters;

    public string CurrentDeleteFilterKey => BuildFilterKey(ActiveFilters);

    public bool HasMatchingDeletePreview =>
        DeletePreviewCount.HasValue &&
        !string.IsNullOrWhiteSpace(DeletePreviewFilterKey) &&
        DeletePreviewFilterKey.Equals(CurrentDeleteFilterKey, StringComparison.Ordinal);

    public IDictionary<string, string> CurrentFilterRouteValues => BuildRouteValues(includeFilters: true);

    public IDictionary<string, string> ClearFilterRouteValues => BuildRouteValues(includeFilters: false);

    public IDictionary<string, string> PreviousPageRouteValues =>
        BuildRouteValues(includeFilters: true, pageNumber: Math.Max(1, PageNumber - 1));

    public IDictionary<string, string> NextPageRouteValues =>
        BuildRouteValues(includeFilters: true, pageNumber: PageNumber + 1);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToSelectedTable();
        }

        try
        {
            var tableData = await themisService.GetTableDataAsync(
                SelectedTable,
                RowLimitValue,
                PageNumber,
                filters: ActiveFilters,
                cancellationToken: HttpContext.RequestAborted);

            var headers = tableData.Columns.Select(col => col.Name).ToArray();
            var rows = ToExcelRows(tableData);
            var file = excelWorkbookService.CreateWorkbook(headers, rows, "Data");
            var fileName = BuildExcelFileName(SelectedTable.TableName, $"top{RowLimitValue}_p{PageNumber}");

            AddExportSummaryHeaders(tableData.Rows.Count, headers.Length);
            return File(file, ExcelContentType, fileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToSelectedTable();
        }
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToSelectedTable();
        }

        try
        {
            var tableData = await themisService.GetTableDataAsync(
                SelectedTable,
                RowLimitValue,
                PageNumber,
                filters: ActiveFilters,
                cancellationToken: HttpContext.RequestAborted);

            var headers = tableData.Columns.Select(col => col.Name).ToArray();
            var rows = ToExcelRows(tableData);
            var file = excelWorkbookService.CreateSemicolonCsv(headers, rows);
            var fileName = BuildCsvFileName(SelectedTable.TableName, $"top{RowLimitValue}_p{PageNumber}");

            AddExportSummaryHeaders(tableData.Rows.Count, headers.Length);
            return File(file, CsvContentType, fileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToSelectedTable();
        }
    }

    public async Task<IActionResult> OnGetExportTemplateAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToSelectedTable();
        }

        try
        {
            var tableData = await themisService.GetTableDataAsync(
                SelectedTable,
                rowLimit: 10,
                pageNumber: 1,
                cancellationToken: HttpContext.RequestAborted);

            var headers = tableData.InsertableColumns.Select(col => col.Name).ToArray();
            if (headers.Length == 0)
            {
                ErrorMessage = localizer["Themis.Error.NoImportColumns"];
                return RedirectToSelectedTable();
            }

            var file = excelWorkbookService.CreateWorkbook(headers, [], "Template");
            var fileName = BuildExcelFileName(SelectedTable.TableName, "template");

            AddExportSummaryHeaders(tableData.Rows.Count, headers.Length);
            return File(file, ExcelContentType, fileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToSelectedTable();
        }
    }

    public async Task<IActionResult> OnPostImportExcelAsync(IFormFile? excelFile)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        if (excelFile is null || excelFile.Length == 0)
        {
            ErrorMessage = localizer["Themis.Error.SelectExcelFile"];
            return RedirectToSelectedTable();
        }

        if (!ExcelFileNames.IsSupportedUpload(excelFile.FileName))
        {
            ErrorMessage = localizer["Themis.Error.InvalidExcelFile"];
            return RedirectToSelectedTable();
        }

        try
        {
            var tableData = await themisService.GetTableDataAsync(
                SelectedTable,
                rowLimit: 10,
                pageNumber: 1,
                cancellationToken: HttpContext.RequestAborted);

            if (tableData.InsertableColumns.Count == 0)
            {
                ErrorMessage = localizer["Themis.Error.NoImportColumns"];
                return RedirectToSelectedTable();
            }

            using var stream = excelFile.OpenReadStream();
            var worksheetData = excelWorkbookService.ReadWorkbook(stream);
            var validationError = ValidateExcelHeaders(worksheetData.Headers, tableData.Columns);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                ErrorMessage = validationError;
                return RedirectToSelectedTable();
            }

            if (worksheetData.Rows.Count == 0)
            {
                ErrorMessage = localizer["Themis.Error.EmptyExcelFile"];
                return RedirectToSelectedTable();
            }

            var token = await SaveImportPreviewAsync(
                SelectedTable.TableName,
                excelFile.FileName,
                worksheetData.Headers,
                worksheetData.Rows,
                CurrentUserName,
                HttpContext.RequestAborted);

            ImportPreviewToken = token;
            var ignoredHeaders = worksheetData.Headers
                .Where(ExchangeTableColumn.IsSystemManagedColumnName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            SuccessMessage = ignoredHeaders.Length == 0
                ? string.Format(localizer["Themis.Success.ImportPreviewPrepared"].Value, worksheetData.Rows.Count)
                : string.Format(localizer["Themis.Success.ImportPreviewPreparedIgnored"].Value, worksheetData.Rows.Count, string.Join(", ", ignoredHeaders));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is UnauthorizedAccessException
                ? GetImportPreviewAccessDeniedMessage()
                : ex.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostConfirmImportExcelAsync(string? importToken)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        var preview = await LoadImportPreviewAsync(importToken, HttpContext.RequestAborted);
        if (preview is null)
        {
            ErrorMessage = localizer["Themis.Error.PreviewMissing"];
            return RedirectToSelectedTable();
        }

        if (!preview.TableName.Equals(SelectedTable.TableName, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = localizer["Themis.Error.PreviewTableMismatch"];
            return RedirectToSelectedTable();
        }

        try
        {
            var rows = preview.Rows
                .Select(row => (IReadOnlyDictionary<string, string?>)row)
                .ToArray();
            var insertedRows = await themisService.AddRowsAsync(
                SelectedTable,
                rows,
                CurrentUserName,
                HttpContext.RequestAborted);

            DeleteImportPreview(preview.Token);

            if (insertedRows == 0)
            {
                ErrorMessage = localizer["Themis.Error.EmptyImportRows"];
                return RedirectToSelectedTable();
            }

            SuccessMessage = insertedRows == 1
                ? string.Format(localizer["Themis.Success.ImportRowInserted"].Value, SelectedTable.TableName)
                : string.Format(localizer["Themis.Success.ImportRowsInserted"].Value, insertedRows, SelectedTable.TableName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ImportPreviewToken = preview.Token;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostAddRowAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        try
        {
            await themisService.AddRowAsync(
                SelectedTable,
                Request.Form,
                CurrentUserName,
                HttpContext.RequestAborted);
            SuccessMessage = string.Format(localizer["Themis.Success.RowAdded"].Value, SelectedTable.TableName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostDeleteRowAsync(string? keyValue)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(keyValue))
        {
            ErrorMessage = localizer["Themis.Error.DeleteRowKeyMissing"];
            return RedirectToSelectedTable();
        }

        try
        {
            await themisService.DeleteRowAsync(
                SelectedTable,
                keyValue,
                CurrentUserName,
                HttpContext.RequestAborted);
            SuccessMessage = localizer["Themis.Success.RowDeleted"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostPreviewDeleteFilteredRowsAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        if (!HasActiveFilters)
        {
            ErrorMessage = localizer["Themis.Error.DeletePreviewRequiresFilter"];
            return RedirectToSelectedTable();
        }

        try
        {
            DeletePreviewCount = await themisService.CountRowsByFilterAsync(
                SelectedTable,
                ActiveFilters,
                HttpContext.RequestAborted);
            DeletePreviewFilterKey = CurrentDeleteFilterKey;
            SuccessMessage = string.Format(localizer["Themis.Success.DeletePreviewFound"].Value, DeletePreviewCount);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostDeleteFilteredRowsAsync(string? deletePreviewKey)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        if (!HasActiveFilters)
        {
            ErrorMessage = localizer["Themis.Error.DeleteRequiresFilter"];
            return RedirectToSelectedTable();
        }

        if (string.IsNullOrWhiteSpace(deletePreviewKey) ||
            !deletePreviewKey.Equals(CurrentDeleteFilterKey, StringComparison.Ordinal))
        {
            ErrorMessage = localizer["Themis.Error.DeletePreviewRequired"];
            return RedirectToSelectedTable();
        }

        try
        {
            var deletedRows = await themisService.DeleteRowsByFilterAsync(
                SelectedTable,
                ActiveFilters,
                CurrentUserName,
                HttpContext.RequestAborted);

            SuccessMessage = deletedRows == 1
                ? localizer["Themis.Success.FilteredRowDeleted"]
                : string.Format(localizer["Themis.Success.FilteredRowsDeleted"].Value, deletedRows);
            DeletePreviewCount = null;
            DeletePreviewFilterKey = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToSelectedTable();
    }

    public async Task<IActionResult> OnPostUpdateRowsAsync(
        [FromBody] ExchangeTableUpdateRequest request)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        TableName = request.TableName;
        SelectTable();

        if (SelectedTable is null)
        {
            return BadRequest(localizer["Themis.Error.TableNotFound"].Value);
        }

        try
        {
            await themisService.UpdateRowsAsync(
                SelectedTable,
                request.Updates,
                CurrentUserName,
                HttpContext.RequestAborted);

            return new JsonResult(new { ok = true });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public async Task<IActionResult> OnPostRevertLogAsync(long logId)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        SelectTable();

        if (SelectedTable is null)
        {
            ErrorMessage = localizer["Themis.Error.TableNotFound"];
            return RedirectToPage();
        }

        try
        {
            await themisService.RevertLogEntryAsync(
                SelectedTable,
                logId,
                CurrentUserName,
                HttpContext.RequestAborted);
            SuccessMessage = string.Format(localizer["Themis.Success.AuditReverted"].Value, logId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToSelectedTable();
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
        var dt = column.DataType.ToLowerInvariant();
        if (dt.Contains("date") || dt.Contains("time"))
        {
            return dt == "date" ? "date" : "datetime-local";
        }

        return IsNumericColumn(column) ? "number" : "text";
    }

    public string InputStepForColumn(ExchangeTableColumn column)
    {
        if (column.DataType.ToLowerInvariant() is "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real")
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

        return InputTypeForColumn(column) switch
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
        if (!HasAccess)
        {
            return;
        }

        SelectTable();

        if (SelectedTable is not null)
        {
            try
            {
                TableData = await themisService.GetTableDataAsync(
                    SelectedTable,
                    RowLimitValue,
                    PageNumber,
                    filters: ActiveFilters,
                    cancellationToken: HttpContext.RequestAborted);

                AuditLogs = await themisService.ListLogsAsync(
                    SelectedTable,
                    rowLimit: 25,
                    cancellationToken: HttpContext.RequestAborted);

                ImportPreview = await LoadImportPreviewAsync(ImportPreviewToken, HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    private async Task LoadAccessAsync()
    {
        CurrentUserName = userNameResolver.ResolveUserName(User);
        HasAccess = await themisService.HasAccessAsync(User, HttpContext.RequestAborted);
        Tables = HasAccess ? themisService.GetConfiguredTables() : [];
    }

    private void SelectTable()
    {
        SelectedTable = !string.IsNullOrWhiteSpace(TableName)
            ? Tables.FirstOrDefault(t => t.TableName.Equals(TableName, StringComparison.OrdinalIgnoreCase))
            : Tables.FirstOrDefault();

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
        var count = new[] { FilterColumns.Count, FilterOperators.Count, FilterValues.Count }.Max();
        if (count == 0)
        {
            return [];
        }

        var filters = new List<ExchangeTableFilter>();
        for (var i = 0; i < count; i++)
        {
            var filter = new ExchangeTableFilter(
                GetAt(FilterColumns, i).Trim(),
                NormalizeOperator(GetAt(FilterOperators, i)),
                GetAt(FilterValues, i).Trim(),
                i == 0 ? ExchangeTableFilter.AndConnector
                    : GetAt(FilterConnectors, i).Equals(ExchangeTableFilter.OrConnector, StringComparison.OrdinalIgnoreCase)
                        ? ExchangeTableFilter.OrConnector
                        : ExchangeTableFilter.AndConnector);

            if (filter.IsActive)
            {
                filters.Add(filter);
            }
        }

        return filters;
    }

    private IDictionary<string, string> BuildRouteValues(bool includeFilters, int? pageNumber = null)
    {
        var route = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rowLimit"] = RowLimitValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["pageNumber"] = Math.Max(1, pageNumber ?? PageNumber).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var tableName = SelectedTable?.TableName ?? TableName;
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            route["tableName"] = tableName;
        }

        if (!includeFilters)
        {
            return route;
        }

        for (var i = 0; i < ActiveFilters.Count; i++)
        {
            var f = ActiveFilters[i];
            route[$"FilterColumns[{i}]"] = f.ColumnName;
            route[$"FilterOperators[{i}]"] = f.Operator;
            route[$"FilterValues[{i}]"] = f.Value;
            route[$"FilterConnectors[{i}]"] = f.Connector;
        }

        return route;
    }

    private static string GetAt(IReadOnlyList<string> list, int index)
    {
        return index < list.Count ? list[index] ?? string.Empty : string.Empty;
    }

    private static int NormalizeRowLimit(string rowLimit)
    {
        return int.TryParse(rowLimit, out var parsed) && parsed is 10 or 100 or 1000 ? parsed : 10;
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
        return column.DataType.ToLowerInvariant() is
            "tinyint" or "smallint" or "int" or "bigint" or
            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real";
    }

    private static string NormalizeOperator(string op)
    {
        return op switch
        {
            "!=" or "<>" => ExchangeTableFilter.NotEqualsOperator,
            ">" => ExchangeTableFilter.GreaterThanOperator,
            ">=" => ExchangeTableFilter.GreaterThanOrEqualOperator,
            "<" => ExchangeTableFilter.LessThanOperator,
            "<=" => ExchangeTableFilter.LessThanOrEqualOperator,
            _ when op.Equals(ExchangeTableFilter.LikeOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.LikeOperator,
            _ when op.Equals(ExchangeTableFilter.InOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.InOperator,
            _ when op.Equals(ExchangeTableFilter.BetweenOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.BetweenOperator,
            _ when op.Equals(ExchangeTableFilter.NotEqualsOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.NotEqualsOperator,
            _ when op.Equals(ExchangeTableFilter.GreaterThanOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.GreaterThanOperator,
            _ when op.Equals(ExchangeTableFilter.GreaterThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.GreaterThanOrEqualOperator,
            _ when op.Equals(ExchangeTableFilter.LessThanOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.LessThanOperator,
            _ when op.Equals(ExchangeTableFilter.LessThanOrEqualOperator, StringComparison.OrdinalIgnoreCase) => ExchangeTableFilter.LessThanOrEqualOperator,
            _ => ExchangeTableFilter.EqualsOperator
        };
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
        if (string.IsNullOrWhiteSpace(token) || token.Any(c => !Uri.IsHexDigit(c)))
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
        return Path.Combine(environment.ContentRootPath, "App_Data", "Themis", "ImportPreviews");
    }

    private string GetImportPreviewAccessDeniedMessage()
    {
        return string.Format(localizer["Themis.Error.ImportAccessDenied"].Value, GetImportPreviewDirectory());
    }

    private string GetImportPreviewPath(string token)
    {
        return Path.Combine(GetImportPreviewDirectory(), $"{token}.json");
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ToExcelRows(ExchangeTableData tableData)
    {
        return tableData.Rows
            .Select(row => (IReadOnlyDictionary<string, string?>)tableData.Columns.ToDictionary(
                col => col.Name,
                col => row.Values.TryGetValue(col.Name, out var value) ? value : string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private string? ValidateExcelHeaders(
        IReadOnlyList<string> headers,
        IReadOnlyList<ExchangeTableColumn> columns)
    {
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            return localizer["Themis.Excel.EmptyHeader"];
        }

        var duplicates = headers
            .Where(h => !ExchangeTableColumn.IsSystemManagedColumnName(h))
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            return string.Format(localizer["Themis.Excel.DuplicateHeaders"].Value, string.Join(", ", duplicates));
        }

        var expectedHeaders = columns
            .Where(col => col.CanInsert)
            .Select(col => col.Name)
            .ToArray();
        var knownHeaders = columns
            .Select(col => col.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualHeaders = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedHeaderSet = expectedHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingHeaders = expectedHeaders
            .Where(h => !actualHeaders.Contains(h))
            .ToArray();
        var extraHeaders = headers
            .Where(h =>
                !expectedHeaderSet.Contains(h) &&
                !knownHeaders.Contains(h) &&
                !ExchangeTableColumn.IsSystemManagedColumnName(h))
            .ToArray();

        if (missingHeaders.Length == 0 && extraHeaders.Length == 0)
        {
            return null;
        }

        var details = new List<string>();
        if (missingHeaders.Length > 0)
        {
            details.Add(string.Format(localizer["Themis.Excel.MissingHeaders"].Value, string.Join(", ", missingHeaders)));
        }

        if (extraHeaders.Length > 0)
        {
            details.Add(string.Format(localizer["Themis.Excel.ExtraHeaders"].Value, string.Join(", ", extraHeaders)));
        }

        return string.Format(localizer["Themis.Excel.StructureMismatch"].Value, string.Join("; ", details));
    }

    private void AddExportSummaryHeaders(int rowCount, int columnCount)
    {
        Response.Headers.Append("X-Export-Row-Count", rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Response.Headers.Append("X-Export-Column-Count", columnCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string BuildCsvFileName(string tableName, string suffix)
    {
        return Path.ChangeExtension(BuildExcelFileName(tableName, suffix), ".csv")!;
    }

    private static string BuildExcelFileName(string tableName, string suffix)
    {
        var fileName = tableName
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal);

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(ch, '_');
        }

        return $"{fileName}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
    }
}




