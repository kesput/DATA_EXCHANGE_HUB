namespace DATA_EXCHANGE_HUB.Services;

public interface IHubSecurityAlertService
{
    Task NotifySuspiciousLoginAsync(
        string activeUserName,
        string attemptedUserName,
        string remoteAddress,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}