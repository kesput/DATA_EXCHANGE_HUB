using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Globalization;

namespace DATA_EXCHANGE_HUB.Pages;

public sealed class IndexModel(
    IHubAccessRequestService accessRequestService,
    IUserNameResolver userNameResolver,
    IGlossaryService glossaryService,
    ISphinxService sphinxService,
    IHubFeedbackService feedbackService) : PageModel
{
    public IReadOnlyList<string> RequestableApps { get; } =
    [
        "Data exchange",
        "ORACUL",
        "KAIROS",
        "ATLAS",
        "HYDRA",
        "ARETE",
        "APIS",
        "ARGUS",
        "THEMIS",
        "SPHINX",
        "File upload"
    ];

    [BindProperty]
    public string DomainUser { get; set; } = string.Empty;

    [BindProperty]
    public string AppName { get; set; } = "HYDRA";

    [BindProperty]
    public string RequestedAccess { get; set; } = "USER";

    [BindProperty]
    public string Comment { get; set; } = string.Empty;

    [BindProperty]
    public string SuggestionMessage { get; set; } = string.Empty;

    [BindProperty]
    public string QuickPollOption { get; set; } = string.Empty;

    [BindProperty]
    public int QuickPollId { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? FeedbackSuccessMessage { get; set; }

    [TempData]
    public string? FeedbackErrorMessage { get; set; }

    public IReadOnlyList<GlossaryTermView> GlossaryTerms { get; private set; } = [];
    public bool IsDomainUserResolved => !string.IsNullOrWhiteSpace(userNameResolver.ResolveUserName(User));
    public IReadOnlyList<SphinxSurveyCard> ActiveSurveys { get; private set; } = [];
    public IReadOnlyList<SphinxSurveyCard> QuickPolls { get; private set; } = [];
    public SphinxSurveyDetail? QuickPoll { get; private set; }
    public IReadOnlyDictionary<int, int> QuickPollResults { get; private set; } = new Dictionary<int, int>();
    public int QuickPollIndex { get; private set; }
    public int? PreviousQuickPollId { get; private set; }
    public int? NextQuickPollId { get; private set; }
    public string? PublicFeedbackLoadError { get; private set; }

    public string AuthorizationNotice =>
        "Norint atlikti veiksma, bus paprasyta Windows autorizacijos. Taip sistema automatiskai priskirs jusu domeno vartotoja.";

    public async Task OnGetAsync()
    {
        DomainUser = userNameResolver.ResolveUserName(User);
        await LoadPageDataAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var resolvedUserName = userNameResolver.ResolveUserName(User);
        DomainUser = string.IsNullOrWhiteSpace(resolvedUserName)
            ? DomainUser.Trim()
            : resolvedUserName;

        if (string.IsNullOrWhiteSpace(DomainUser))
        {
            return Challenge(IISDefaults.AuthenticationScheme);
        }

        if (!RequestableApps.Contains(AppName, StringComparer.OrdinalIgnoreCase))
        {
            ErrorMessage = "Pasirinkta netinkama aplikacija.";
            return RedirectToPage();
        }

        if (!RequestedAccess.Equals("USER", StringComparison.OrdinalIgnoreCase) &&
            !RequestedAccess.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Pasirinktas netinkamas prieigos tipas.";
            return RedirectToPage();
        }

        try
        {
            await accessRequestService.SaveAsync(
                new HubAccessRequest(
                    DomainUser,
                    AppName,
                    RequestedAccess.ToUpperInvariant(),
                    Comment.Trim()),
                HttpContext.RequestAborted);
            SuccessMessage = "Prieigos prasymas uzregistruotas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSuggestionAsync()
    {
        var userName = ResolvePublicRespondent();
        if (string.IsNullOrWhiteSpace(SuggestionMessage))
        {
            FeedbackErrorMessage = "Iveskite pasiulyma.";
            return RedirectToPage();
        }

        try
        {
            await feedbackService.SaveSuggestionAsync(
                new HubSuggestionRequest(userName, SuggestionMessage),
                HttpContext.RequestAborted);
            FeedbackSuccessMessage = "Aciu, pasiulymas issaugotas.";
        }
        catch (Exception ex)
        {
            FeedbackErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostQuickPollAsync()
    {
        var respondent = userNameResolver.ResolveUserName(User);
        if (string.IsNullOrWhiteSpace(respondent))
        {
            return Challenge(IISDefaults.AuthenticationScheme);
        }

        if (string.IsNullOrWhiteSpace(QuickPollOption))
        {
            FeedbackErrorMessage = "Pasirinkite balsavimo varianta.";
            return RedirectToPage();
        }

        try
        {
            var poll = QuickPollId > 0
                ? await sphinxService.GetSurveyDetailAsync(QuickPollId, respondent, HttpContext.RequestAborted)
                : await sphinxService.GetLatestQuickPollAsync(respondent, HttpContext.RequestAborted);
            if (!string.Equals(poll?.Survey.SurveyKind, "POLL", StringComparison.OrdinalIgnoreCase))
            {
                poll = null;
            }
            if (poll?.TopLevelQuestions.FirstOrDefault()?.Options.Any(option => option.Id.ToString(CultureInfo.InvariantCulture) == QuickPollOption) != true)
            {
                throw new InvalidOperationException("Pasirinktas netinkamas balsavimo variantas.");
            }

            var answersJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new
                {
                    questionId = poll.TopLevelQuestions[0].Id,
                    text = (string?)null,
                    optionIds = new[] { int.Parse(QuickPollOption, CultureInfo.InvariantCulture) }
                }
            });
            await sphinxService.SubmitResponseAsync(
                new SphinxSubmitRequest(poll.Survey.Id, respondent, answersJson),
                HttpContext.RequestAborted);
            FeedbackSuccessMessage = "Balsas issaugotas.";
        }
        catch (Exception ex)
        {
            FeedbackErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        GlossaryTerms = await glossaryService.ListAsync(
            CultureInfo.CurrentUICulture,
            HttpContext.RequestAborted);

        var respondent = ResolvePublicRespondent();
        try
        {
            ActiveSurveys = await sphinxService.GetSurveyCardsAsync(
                respondent,
                HttpContext.RequestAborted);
            QuickPolls = (await sphinxService.GetQuickPollCardsAsync(
                    respondent,
                    HttpContext.RequestAborted))
                .OrderBy(poll => poll.HasResponded)
                .ThenByDescending(poll => poll.Id)
                .ToArray();
            var requestedPollId = GetRequestedPollId();
            var selectedCard = QuickPolls.FirstOrDefault(poll => poll.Id == requestedPollId) ??
                               QuickPolls.FirstOrDefault();
            QuickPollIndex = selectedCard is null
                ? 0
                : QuickPolls.ToList().FindIndex(poll => poll.Id == selectedCard.Id);
            QuickPoll = selectedCard is null
                ? null
                : await sphinxService.GetSurveyDetailAsync(
                    selectedCard.Id,
                    respondent,
                    HttpContext.RequestAborted);
            QuickPollResults = QuickPoll is null
                ? new Dictionary<int, int>()
                : await sphinxService.GetQuickPollResultsAsync(QuickPoll.Survey.Id, HttpContext.RequestAborted);
            if (QuickPolls.Count > 1 && QuickPollIndex >= 0)
            {
                PreviousQuickPollId = QuickPolls[(QuickPollIndex - 1 + QuickPolls.Count) % QuickPolls.Count].Id;
                NextQuickPollId = QuickPolls[(QuickPollIndex + 1) % QuickPolls.Count].Id;
            }
        }
        catch (Exception ex)
        {
            PublicFeedbackLoadError = ex.Message;
            ActiveSurveys = [];
            QuickPoll = null;
        }
    }

    private int? GetRequestedPollId()
    {
        return int.TryParse(Request.Query["poll"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollId)
            ? pollId
            : null;
    }

    private string ResolvePublicRespondent()
    {
        var userName = userNameResolver.ResolveUserName(User);
        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        if (!HttpContext.Session.TryGetValue("PublicFeedbackRespondent", out var value) ||
            value.Length == 0)
        {
            userName = $"public-{Guid.NewGuid():N}";
            HttpContext.Session.SetString("PublicFeedbackRespondent", userName);
            return userName;
        }

        return System.Text.Encoding.UTF8.GetString(value);
    }
}

