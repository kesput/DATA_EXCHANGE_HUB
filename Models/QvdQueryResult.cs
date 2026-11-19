namespace DATA_EXCHANGE_HUB.Models;

public sealed record QvdQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    IReadOnlySet<string> NumericColumns,
    string ValueCulture,
    QvdReadStatistics? ReadStatistics = null);

public sealed record QvdReadStatistics(int Files, long ScannedRows, int RetainedRows, long RetainedBytes,
    IReadOnlyDictionary<string, int> RetainedRowsBySource);
