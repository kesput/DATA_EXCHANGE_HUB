namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeTableRow(
    IReadOnlyDictionary<string, string> Values,
    string? KeyValue);
