namespace DATA_EXCHANGE_HUB.Models;

public sealed record HubApplication
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Page { get; init; } = string.Empty;
    public string IconText { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
