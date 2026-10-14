using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IHubFeedbackService
{
    Task SaveSuggestionAsync(HubSuggestionRequest request, CancellationToken cancellationToken = default);
}
