using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface ISphinxService
{
    Task<SphinxAccessState> GetAccessAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task CreateSurveyAsync(SphinxCreateSurveyRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SphinxSurveyCard>> GetSurveyCardsAsync(string respondent, CancellationToken cancellationToken = default);
    Task<SphinxSurveyDetail?> GetSurveyDetailAsync(int id, string respondent, CancellationToken cancellationToken = default);
    Task SubmitResponseAsync(SphinxSubmitRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SphinxSurveyCard>> GetQuickPollCardsAsync(string respondent, CancellationToken cancellationToken = default);
    Task<SphinxSurveyDetail?> GetLatestQuickPollAsync(string respondent, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, int>> GetQuickPollResultsAsync(int surveyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SphinxManagementItem>> GetManagementListAsync(string currentUserName, bool isAdmin, CancellationToken cancellationToken = default);
    Task<SphinxSurveyDetail?> GetSurveyForOwnerAsync(int id, string currentUserName, bool isAdmin, CancellationToken cancellationToken = default);
    Task CloseSurveyAsync(int id, string currentUserName, bool isAdmin, CancellationToken cancellationToken = default);
    Task<SphinxResultsExport?> GetResultsExportAsync(int id, string currentUserName, bool isAdmin, CancellationToken cancellationToken = default);
}

public sealed record SphinxAccessState(bool CanOpen, bool IsAdmin);
