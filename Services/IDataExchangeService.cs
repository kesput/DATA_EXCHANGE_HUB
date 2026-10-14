using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IDataExchangeService
{
    Task<IReadOnlyList<ExchangeRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task<ExchangeRecord> SaveAsync(IFormFile file, string uploadedBy, string? notes, CancellationToken cancellationToken = default);

    Task<ExchangeDownload?> GetDownloadAsync(Guid id, CancellationToken cancellationToken = default);
}
