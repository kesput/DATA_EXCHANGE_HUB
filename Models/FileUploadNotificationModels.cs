namespace DATA_EXCHANGE_HUB.Models;

public sealed record FileUploadNotificationSubscription(
    int Id,
    string ActorUser,
    string FileNamePattern,
    string TriggerType,
    string NotifyChannel,
    string Email,
    string PhoneNo,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdateTime);

public sealed record FileUploadNotificationEvent(
    string FileName,
    string FilePath,
    long FileSizeBytes,
    string ActorUser,
    string TriggerType,
    DateTimeOffset EventAt);
