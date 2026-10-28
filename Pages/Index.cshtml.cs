using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Globalization;
using System.Text.Json;

namespace DATA_EXCHANGE_HUB.Pages;

public sealed class IndexModel(
    IHubAccessRequestService accessRequestService,
    IUserNameResolver userNameResolver,
    IGlossaryService glossaryService,
    ISphinxService sphinxService,
    IHubFeedbackService feedbackService,
    IAuthenticationSchemeProvider authenticationSchemes) : PageModel
{
    private const string AccessRequestDraftKey = "Hub.AccessRequest.Draft";
    private sealed record AccessRequestDraft(string AppName, string RequestedAccess, string Comment, DateTimeOffset ExpiresAt);

    public IReadOnlyList<string> RequestableApps { get; } =
    [
        "Data exchange",
        "ORACUL",
        "KAIROS",
        "ATLAS",
        "HYDRA",
        "DIKE",
        "ARETE",
        "APIS",
        "ARGUS",
        "THEMIS",
        "SPHINX",
        "File upload",
        "Send Mail File",
        "Klientu info",
        "SESHAT",
        "IRIS",
        "SEND MAIL"
    ];

    [BindProperty]
    public string DomainUser { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
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
        "Hub.Security.RequestIdentity";

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        DomainUser = userNameResolver.ResolveUserName(User);
        if (!string.IsNullOrWhiteSpace(DomainUser)) RestoreAccessRequestDraft();
        await LoadPageDataAsync();
    }

    public async Task<IActionResult> OnGetAuthorizeAccessRequestAsync()
    {
        Response.Headers.CacheControl = "no-store";
        DomainUser = userNameResolver.ResolveUserName(User);
        if (string.IsNullOrWhiteSpace(DomainUser))
        {
            if (await authenticationSchemes.GetSchemeAsync(IISDefaults.AuthenticationScheme) is not null)
                return Challenge(IISDefaults.AuthenticationScheme);

            // Kestrel without a Windows handler cannot display a native login prompt.
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            ErrorMessage = "Hub.Security.WindowsRequired";
            return Page();
        }

        return RedirectToPage("/Index", pageHandler: null,
            routeValues: new { AppName = RequestableApps.Contains(AppName, StringComparer.OrdinalIgnoreCase) ? AppName : "HYDRA" },
            fragment: "access-request");
    }

    public IActionResult OnPostAuthenticateAccessRequest()
    {
        var draft = new AccessRequestDraft(
            RequestableApps.Contains(AppName, StringComparer.OrdinalIgnoreCase) ? AppName : "HYDRA",
            string.Equals(RequestedAccess, "ADMIN", StringComparison.OrdinalIgnoreCase) ? "ADMIN" : "USER",
            (Comment ?? string.Empty)[..Math.Min(Comment?.Length ?? 0, 1000)],
            DateTimeOffset.UtcNow.AddMinutes(10));
        HttpContext.Session.SetString(AccessRequestDraftKey, JsonSerializer.Serialize(draft));
        return RedirectToPage("/Index", "AuthorizeAccessRequest", new { draft.AppName });
    }

    private void RestoreAccessRequestDraft()
    {
        var json = HttpContext.Session.GetString(AccessRequestDraftKey);
        if (json is null) return;
        HttpContext.Session.Remove(AccessRequestDraftKey);
        try
        {
            var draft = JsonSerializer.Deserialize<AccessRequestDraft>(json);
            if (draft is null || draft.ExpiresAt < DateTimeOffset.UtcNow ||
                !RequestableApps.Contains(draft.AppName, StringComparer.OrdinalIgnoreCase) ||
                draft.RequestedAccess is not ("USER" or "ADMIN")) return;
            AppName = draft.AppName;
            RequestedAccess = draft.RequestedAccess;
            Comment = draft.Comment;
            // GET-bound values must not override the restored form in tag helpers.
            ModelState.Remove(nameof(AppName));
        }
        catch (JsonException) { /* An invalid draft is discarded; it never grants access. */ }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var resolvedUserName = userNameResolver.ResolveUserName(User);
        DomainUser = resolvedUserName;

        if (string.IsNullOrWhiteSpace(DomainUser))
        {
            return OnPostAuthenticateAccessRequest();
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

