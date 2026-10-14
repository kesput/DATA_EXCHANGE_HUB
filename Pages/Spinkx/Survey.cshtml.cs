using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Server.IISIntegration;

namespace DATA_EXCHANGE_HUB.Pages.Spinkx;

public sealed class SurveyModel(
    ISphinxService sphinxService,
    IUserNameResolver userNameResolver) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public SphinxSurveyDetail? Survey { get; private set; }

    public string Respondent { get; private set; } = string.Empty;

    [BindProperty]
    public string AnswersJson { get; set; } = string.Empty;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string AuthorizationNotice =>
        "Norint pateikti atsakymus, bus paprasyta Windows autorizacijos. Taip apklausa bus susieta su jusu domeno vartotoju.";

    public async Task<IActionResult> OnGetAsync()
    {
        Respondent = userNameResolver.ResolveUserName(User);
        Survey = await sphinxService.GetSurveyDetailAsync(Id, Respondent, HttpContext.RequestAborted);
        if (Survey is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Respondent = userNameResolver.ResolveUserName(User);
        if (string.IsNullOrWhiteSpace(Respondent))
        {
            return Challenge(IISDefaults.AuthenticationScheme);
        }

        if (string.IsNullOrWhiteSpace(AnswersJson))
        {
            ErrorMessage = "Prašome atsakyti į klausimus.";
            return RedirectToPage(new { id = Id });
        }

        try
        {
            await sphinxService.SubmitResponseAsync(
                new SphinxSubmitRequest(Id, Respondent, AnswersJson),
                HttpContext.RequestAborted);

            SuccessMessage = "Atsakymai išsaugoti.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage("/Spinkx/Index");
    }
}
