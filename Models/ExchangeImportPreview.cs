namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeImportPreview(
    string Token,
    string TableName,
    string FileName,
    string UserName,
    DateTimeOffset CreatedAt,
    List<string> Headers,
    List<string> IgnoredHeaders,
    List<Dictionary<string, string?>> Rows);
