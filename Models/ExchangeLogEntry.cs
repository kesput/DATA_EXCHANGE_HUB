namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeLogEntry(
    long LogId,
    DateTimeOffset EventAt,
    string ActionType,
    string Source,
    string TableName,
    string? KeyColumn,
    string? KeyValue,
    string ActorUser,
    string? ChangedColumns,
    bool IsReverted,
    DateTimeOffset? RevertedAt,
    string? RevertedBy)
{
    public bool CanRevert =>
        !IsReverted &&
        (ActionType.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) ||
         ActionType.Equals("DELETE", StringComparison.OrdinalIgnoreCase));
}
