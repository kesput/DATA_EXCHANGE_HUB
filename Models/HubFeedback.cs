namespace DATA_EXCHANGE_HUB.Models;

public sealed record HubSuggestionRequest(
    string UserName,
    string Message);
