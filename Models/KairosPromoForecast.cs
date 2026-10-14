namespace DATA_EXCHANGE_HUB.Models;

public sealed record KairosPromoForecast(
    int Id,
    string Client,
    string InfPrek,
    DateTime? BuyStart,
    DateTime PromoStart,
    DateTime PromoEnd,
    decimal? Qty,
    DateTime? InfoDate,
    string? Status,
    decimal? DailySales,
    string KeyValue,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    DateTimeOffset UpdateTime,
    string UpdatedBy);

public sealed record KairosPromoForecastImportResult(
    int InsertedRows,
    int UpdatedRows,
    int IgnoredColumns,
    IReadOnlyList<string> IgnoredHeaders)
{
    public int SavedRows => InsertedRows + UpdatedRows;
}
