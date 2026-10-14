using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IFileUploadNotificationService
{
    Task<IReadOnlyList<FileUploadNotificationSubscription>> ListForUserAsync(
        string userName,
        CancellationToken cancellationToken = default);

    Task SaveSubscriptionAsync(
        string userName,
        string fileNamePattern,
        string triggerType,
        string notifyChannel,
        string contact,
        CancellationToken cancellationToken = default);

    Task CancelSubscriptionAsync(
        int subscriptionId,
        string userName,
        CancellationToken cancellationToken = default);

    Task NotifyAsync(
        FileUploadNotificationEvent uploadEvent,
        CancellationToken cancellationToken = default);
}
