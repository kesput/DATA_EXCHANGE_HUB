namespace DATA_EXCHANGE_HUB.Models;

public sealed record ApisQvdRoot(
    string Path,
    bool Exists);

public sealed record ApisQvdFile(
    string Id,
    string Name,
    string RelativePath,
    long SizeBytes,
    DateTime LastModified,
    string? CreatorDoc,
    DateTime? CreateUtcTime);

public sealed record ApisQvdField(
    string Name,
    string? NumberFormat,
    int? SymbolCount);

public sealed record ApisQvdPreview(
    ApisQvdFile File,
    int TotalRecords,
    IReadOnlyList<ApisQvdField> Fields,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    string? Warning);
