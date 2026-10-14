namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeTableData(
    string TableName,
    IReadOnlyList<ExchangeTableColumn> Columns,
    IReadOnlyList<ExchangeUniqueKey> UniqueKeys,
    IReadOnlyList<ExchangeTableRow> Rows,
    string? KeyColumn,
    int RowLimit,
    int PageNumber,
    bool HasNextPage)
{
    public IReadOnlyList<ExchangeTableColumn> InsertableColumns =>
        Columns.Where(column => column.CanInsert).ToArray();

    public bool CanUpdateRows => !string.IsNullOrWhiteSpace(KeyColumn);

    public bool HasPreviousPage => PageNumber > 1;
}
