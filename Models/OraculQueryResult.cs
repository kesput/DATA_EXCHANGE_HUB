namespace DATA_EXCHANGE_HUB.Models;

public sealed record OraculQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
