namespace DATA_EXCHANGE_HUB.Models;

public sealed record IrisSourcePreviewRequest(string SourceType, string ConnectionName,
    string? Database, string? SqlText, string? FilePath, string? Procedure,
    long SourceId = 0, long ReportId = 0);

public sealed record IrisPreviewColumn(string Name, string DataType, bool Nullable);

public sealed record IrisPreviewTable(IReadOnlyList<IrisPreviewColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated);

public sealed record IrisSourcePreviewResult(IReadOnlyList<IrisPreviewTable> Tables,
    bool Truncated, int RowLimit, Guid AuditId);

public sealed record IrisSourcePreviewView(IrisSourcePreviewResult? Result = null,
    string? ErrorKey = null, string? ErrorDetail = null);
