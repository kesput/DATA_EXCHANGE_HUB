namespace DATA_EXCHANGE_HUB.Models;

public sealed record KairosLogEntry(
    long LogId,
    DateTimeOffset EventAt,
    string ActionType,
    int? RequestId,
    string ActorUser,
    string? Title,
    DateTime? NeedDate,
    bool IsReverted,
    bool IsLocked)
{
    public bool CanRestore =>
        ActionType.Equals("DELETE", StringComparison.OrdinalIgnoreCase) &&
        !IsReverted &&
        !IsLocked;
}
