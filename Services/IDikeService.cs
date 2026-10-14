using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IDikeService
{
    Task<IReadOnlyList<DikeClientInfo>> ListClientsAsync(
        string searchTerm,
        CancellationToken cancellationToken = default);

    Task<DikeClientInfo?> GetClientAsync(
        long ownerId,
        CancellationToken cancellationToken = default);
}
