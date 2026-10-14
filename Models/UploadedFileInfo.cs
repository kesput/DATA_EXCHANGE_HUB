namespace DATA_EXCHANGE_HUB.Models;

public sealed record UploadedFileInfo(
    string FileName,
    long SizeBytes,
    DateTime LastWriteTime,
    string? UploadedBy,
    DateTimeOffset? UploadedAt);
