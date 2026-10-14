namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeRecord(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    string Status,
    string? Notes);
