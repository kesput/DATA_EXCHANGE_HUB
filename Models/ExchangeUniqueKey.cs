namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeUniqueKey(
    string Name,
    string Type,
    IReadOnlyList<string> Columns)
{
    public string ColumnLabel => string.Join(" + ", Columns);
}
