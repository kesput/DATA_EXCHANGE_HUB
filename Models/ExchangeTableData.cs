namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeTableData(
    string TableName,
    IReadOnlyList<ExchangeTableColumn> Columns,
    IReadOnlyList<ExchangeUniqueKey> UniqueKeys,
    IReadOnlyList<ExchangeTableRow> Rows,
    string? KeyColumn,
    int RowLimit,
    int PageNumber,
    bool HasNextPage,
    IReadOnlyList<string>? RowKeyColumns = null)
{
    public IReadOnlyList<ExchangeTableColumn> InsertableColumns =>
        Columns.Where(column => column.CanInsert).ToArray();

    public IReadOnlyList<string> KeyColumns => RowKeyColumns ?? (string.IsNullOrWhiteSpace(KeyColumn) ? [] : [KeyColumn]);

    public bool IsKeyColumn(string name) => KeyColumns.Contains(name, StringComparer.OrdinalIgnoreCase);

    public bool CanUpdateRows => KeyColumns.Count > 0;

    public bool HasPreviousPage => PageNumber > 1;
}
