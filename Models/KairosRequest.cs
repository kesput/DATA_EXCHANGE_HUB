namespace DATA_EXCHANGE_HUB.Models;

public sealed record KairosRequest(
    int Id,
    string RequestType,
    string Title,
    string InfPrek,
    decimal QuantityRequest,
    DateTime NeedDate,
    DateTime MinExpirationRequest,
    string NetworkClient,
    string Comment,
    string Status,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdateTime,
    string UpdatedBy,
    bool IsLocked)
{
    public string LockLabel => IsLocked ? "Uzrakinta" : "Aktyvu";
}
