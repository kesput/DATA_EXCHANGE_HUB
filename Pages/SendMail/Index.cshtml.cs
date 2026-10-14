using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.SendMail;

public class IndexModel(
    ISendMailService sendMailService,
    IUserNameResolver userNameResolver) : PageModel
{
    [BindProperty]
    public string ApplicationName { get; set; } = "SESHAT";

    [BindProperty]
    public string ProcessName { get; set; } = "Dokumento priminimas";

    [BindProperty]
    public string RecipientEmail { get; set; } = string.Empty;

    [BindProperty]
    public string RecipientName { get; set; } = string.Empty;

    [BindProperty]
    public string Message { get; set; } = "Primename perziureti aktualu dokumenta ir, jei reikia, atnaujinti informacija.";

    public SendMailDraft? Draft { get; private set; }

    public SendMailArchiveResult? ArchiveResult { get; private set; }

    public IReadOnlyList<SendMailLogEntry> RecentMails { get; private set; } = [];

    public string ArchiveRoot { get; private set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadPageStateAsync();
    }

    public async Task OnPostPreviewAsync()
    {
        try
        {
            Draft = BuildDraft();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostSaveDraftAsync()
    {
        try
        {
            Draft = BuildDraft();
            ArchiveResult = await sendMailService.SaveDraftAsync(Draft, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    private SendMailDraft BuildDraft()
    {
        return sendMailService.BuildDraft(
            ApplicationName,
            ProcessName,
            RecipientEmail,
            RecipientName,
            Message,
            userNameResolver.ResolveUserName(User));
    }

    private async Task LoadPageStateAsync()
    {
        ArchiveRoot = sendMailService.GetArchiveRoot();
        RecentMails = await sendMailService.ListRecentAsync(HttpContext.RequestAborted);
    }
}
