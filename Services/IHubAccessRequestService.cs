namespace DATA_EXCHANGE_HUB.Services;

public interface IHubAccessRequestService
{
    Task SaveAsync(
        HubAccessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record HubAccessRequest(
    string DomainUser,
    string AppName,
    string RequestedAccess,
    string Comment);
