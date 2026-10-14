namespace DATA_EXCHANGE_HUB.Models;

public sealed record KairosCreateRequest(
    string Title,
    string InfPrek,
    decimal QuantityRequest,
    DateTime NeedDate,
    DateTime MinExpirationRequest,
    string NetworkClient,
    string Comment);
