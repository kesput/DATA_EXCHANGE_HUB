namespace DATA_EXCHANGE_HUB.Models;

public sealed record ClientInfoResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

public sealed record ClientInfoFilter(
    string Operator,
    string FirmId);
