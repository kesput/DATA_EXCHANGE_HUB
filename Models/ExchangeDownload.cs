namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeDownload(
    string FileName,
    string ContentType,
    Stream Content);
