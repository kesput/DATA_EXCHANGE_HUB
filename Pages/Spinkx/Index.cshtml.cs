using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Spinkx;

public sealed class IndexModel(
    ISphinxService sphinxService,
    IUserNameResolver userNameResolver) : PageModel
{
    public IReadOnlyList<SphinxSurveyCard> Surveys { get; private set; } = [];

    public string Respondent { get; private set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Respondent = userNameResolver.ResolveUserName(User);
        try
        {
            Surveys = await sphinxService.GetSurveyCardsAsync(Respondent, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
