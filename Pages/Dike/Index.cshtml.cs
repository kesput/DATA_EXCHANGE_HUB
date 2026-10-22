using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Pages.Dike;

public sealed class IndexModel(
    IDikeAccessService dikeAccessService,
    IDikeService dikeService,
    DikeAgreementStore agreementStore,
    IUserNameResolver userNameResolver) : PageModel
{
    public string? ErrorMessage { get; private set; }

    [TempData] public string? SuccessMessage { get; set; }
    [BindProperty] public DikeAgreementInput Agreement { get; set; } = new();
    [BindProperty(SupportsGet = true)] public Guid? AgreementId { get; set; }
    [BindProperty(SupportsGet = true)] public bool CreateNewAgreement { get; set; }
    public IReadOnlyList<DikeAgreement> ClientAgreements { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public string? HistorySearch { get; set; }
    public IReadOnlyList<DikeAgreement> Agreements { get; private set; } = [];
    public IReadOnlyList<DikeAudit> History { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public Guid? DocumentAgreementId { get; set; }
    [TempData] public string? DocumentError { get; set; }
    public DikeAgreementInput? DocumentAgreement { get; private set; }
    public IReadOnlyList<DikeDocument> Documents { get; private set; } = [];
    public bool CanDeleteDocuments { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? SelectedOwnerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ActivePane { get; set; } = "agreements";

    [BindProperty(SupportsGet = true)]
    public string? AgreementPane { get; set; } = "active";

    public IReadOnlyList<DikeClientInfo> Clients { get; private set; } = [];

    public DikeClientInfo? SelectedClient { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanOpenAsync())
        {
            ErrorMessage = "Siam Windows vartotojui DIKE aplikacijos teise nesuteikta.";
            return Page();
        }

        try
        {
            if (CreateNewAgreement) AgreementId = null;
            if (AgreementId.HasValue)
            {
                var saved = await agreementStore.GetAsync(AgreementId.Value, HttpContext.RequestAborted);
                if (saved is null) ErrorMessage = "Dike.Validation.NotFound";
                else { Agreement = saved; SelectedOwnerId = saved.OwnerId; AgreementPane = "edit"; }
            }
            else if (SelectedOwnerId.HasValue) Agreement.OwnerId = SelectedOwnerId.Value;
            await LoadClientInfoAsync();
            if (SelectedClient is not null) Agreement.OwnerId = SelectedClient.OwnerId;
            await LoadClientAgreementsAsync();
            if (!AgreementId.HasValue && !CreateNewAgreement && ClientAgreements.Count > 0)
            {
                Agreement = ClientAgreements.OrderByDescending(item => item.Data.IsActive)
                    .ThenByDescending(item => item.UpdatedAt).ThenBy(item => item.Data.Id).First().Data;
                AgreementId = Agreement.Id;
                AgreementPane = "edit";
            }
            await LoadAgreementsAsync();
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await CanOpenAsync()) return Forbid();
        ActivePane = "agreements";
        AgreementPane = "edit";
        SelectedOwnerId = Agreement.OwnerId;
        await LoadClientInfoAsync();
        if (SelectedClient is null) ModelState.AddModelError("Agreement.OwnerId", "Dike.Validation.Client");
        if (ModelState.IsValid)
        {
            try
            {
                var id = await agreementStore.SaveAsync(Agreement, SelectedClient!, userNameResolver.ResolveUserName(User), HttpContext.RequestAborted);
                SuccessMessage = "Dike.Saved";
                return RedirectToPage(new { SelectedOwnerId, AgreementId = id, ActivePane, AgreementPane });
            }
            catch (Exception exception) { ErrorMessage = exception.Message; }
        }
        try { await LoadClientAgreementsAsync(); await LoadAgreementsAsync(); }
        catch (Exception exception) { ErrorMessage ??= exception.Message; }
        return Page();
    }

    public async Task<IActionResult> OnGetSchedulePreviewAsync(string? firstPeriod, string? frequency,
        string? monthDays, string? sendTime, string? validFrom)
    {
        if (!await CanOpenAsync()) return Forbid();
        Response.Headers.CacheControl = "no-store";
        var now = DateTimeOffset.UtcNow;
        var planned = DikeSchedule.Next(firstPeriod ?? "", frequency ?? "", monthDays ?? "", sendTime ?? "", validFrom ?? "", now);
        var previous = DikeSchedule.Previous(firstPeriod ?? "", frequency ?? "", monthDays ?? "", sendTime ?? "", validFrom ?? "", now);
        if (planned is null) return new JsonResult(new { status = "incomplete" });
        var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
        return new JsonResult(new {
            status = "ready",
            date = planned.At.ToString(english ? "dd/MM/yyyy, dddd HH:mm" : "yyyy-MM-dd, dddd HH:mm", System.Globalization.CultureInfo.CurrentUICulture),
            period = planned.Period.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture),
            previousDate = previous?.At.ToString(english ? "dd/MM/yyyy, dddd HH:mm" : "yyyy-MM-dd, dddd HH:mm", System.Globalization.CultureInfo.CurrentUICulture),
            clockAdjusted = planned.ClockAdjusted
        });
    }

    private async Task LoadAgreementsAsync()
    {
        Agreements = await agreementStore.ListAsync(HttpContext.RequestAborted);
        History = await agreementStore.HistoryAsync(SelectedOwnerId, HistorySearch ?? "", HttpContext.RequestAborted);
        DocumentAgreementId ??= AgreementId;
        if (!DocumentAgreementId.HasValue && SelectedOwnerId.HasValue)
        {
            var matches = Agreements.Where(item => item.Data.OwnerId == SelectedOwnerId).Take(2).ToArray();
            if (matches.Length == 1) DocumentAgreementId = matches[0].Data.Id;
        }
        if (DocumentAgreementId.HasValue && await CanAccessDocumentAgreementAsync(DocumentAgreementId.Value))
        {
            DocumentAgreement = await agreementStore.GetAsync(DocumentAgreementId.Value, HttpContext.RequestAborted);
            Documents = await agreementStore.ListDocumentsAsync(DocumentAgreementId.Value, HttpContext.RequestAborted);
        }
        else if (DocumentAgreementId.HasValue) DocumentError ??= "Dike.Document.AccessDenied";
        CanDeleteDocuments = await dikeAccessService.IsAdminAsync(User, HttpContext.RequestAborted);
    }

    private async Task LoadClientAgreementsAsync()
    {
        ClientAgreements = SelectedClient is null ? []
            : await agreementStore.ListAsync(HttpContext.RequestAborted, SelectedClient.OwnerId);
    }

    // Confirmed policy: all DIKE users share documents for clients in DIKE's active SA/BLT scope.
    // An agreement must exist and resolve to an eligible client; arbitrary/out-of-scope IDs are denied.
    private async Task<bool> CanAccessDocumentAgreementAsync(Guid agreementId)
    {
        if (!await CanOpenAsync()) return false;
        var agreement = await agreementStore.GetAsync(agreementId, HttpContext.RequestAborted);
        return agreement is not null && await dikeService.GetClientAsync(agreement.OwnerId, HttpContext.RequestAborted) is not null;
    }

    public async Task<IActionResult> OnPostUploadDocumentAsync(Guid documentAgreementId, IFormFile? documentFile, string? documentNote)
    {
        if (!await CanAccessDocumentAgreementAsync(documentAgreementId)) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            if (documentFile is null) throw new InvalidOperationException("Dike.Document.Required");
            await agreementStore.UploadDocumentAsync(documentAgreementId, documentFile, documentNote,
                userNameResolver.ResolveUserName(User), HttpContext.RequestAborted);
            SuccessMessage = "Dike.Document.Uploaded";
        }
        catch (Exception exception) { DocumentError = exception.Message; }
        return RedirectToPage(new { ActivePane = "history", DocumentAgreementId = documentAgreementId });
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(Guid documentId, Guid documentAgreementId)
    {
        if (!await CanAccessDocumentAgreementAsync(documentAgreementId)) return StatusCode(StatusCodes.Status403Forbidden);
        var file = await agreementStore.GetDocumentAsync(documentId, HttpContext.RequestAborted);
        if (file is null || file.Document.AgreementId != documentAgreementId) return NotFound();
        try
        {
            await agreementStore.DeleteDocumentAsync(documentId, User, userNameResolver.ResolveUserName(User), HttpContext.RequestAborted);
            SuccessMessage = "Dike.Document.Deleted";
        }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); }
        catch (Exception exception) { DocumentError = exception.Message; }
        return RedirectToPage(new { ActivePane = "history", DocumentAgreementId = documentAgreementId });
    }

    public async Task<IActionResult> OnGetDocumentAsync(Guid documentAgreementId, Guid documentId, bool download = false)
    {
        if (!await CanAccessDocumentAgreementAsync(documentAgreementId)) return StatusCode(StatusCodes.Status403Forbidden);
        var file = await agreementStore.GetDocumentAsync(documentId, HttpContext.RequestAborted);
        if (file is null || file.Document.AgreementId != documentAgreementId) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (download) return File(file.Bytes, file.Document.ContentType, file.Document.FileName);
        if (file.Document.ContentType == "application/pdf")
        {
            Response.Headers.ContentDisposition = "inline";
            return new FileContentResult(file.Bytes, "application/pdf") { EnableRangeProcessing = true };
        }
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-src 'self'; img-src data:; base-uri 'none'; form-action 'none'";
        return Partial("_MailPreview", DikeMailReader.Read(file.Bytes));
    }

    public static DateTime VilniusTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius"));

    private async Task LoadClientInfoAsync()
    {
        try
        {
            Clients = await dikeService.ListClientsAsync(
                SearchTerm ?? "",
                HttpContext.RequestAborted);

            if (SelectedOwnerId.HasValue)
            {
                SelectedClient = await dikeService.GetClientAsync(
                    SelectedOwnerId.Value,
                    HttpContext.RequestAborted);
            }

            if (SelectedClient is null && Clients.Count == 1 && !string.IsNullOrWhiteSpace(SearchTerm))
            {
                SelectedClient = Clients[0];
                SelectedOwnerId = SelectedClient.OwnerId;
            }

            if (SelectedClient is null && Clients.Count > 0 && string.Equals(ActivePane, "info", StringComparison.OrdinalIgnoreCase))
            {
                SelectedClient = Clients[0];
                SelectedOwnerId = SelectedClient.OwnerId;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Clients = [];
            SelectedClient = null;
        }
    }

    private async Task<bool> CanOpenAsync()
    {
        try
        {
            return await dikeAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }
}
