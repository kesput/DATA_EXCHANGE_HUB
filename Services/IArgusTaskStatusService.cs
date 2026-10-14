using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IArgusTaskStatusService
{
    Task<ArgusTaskStatusDashboard> GetDashboardAsync(
        DateTime date,
        CancellationToken cancellationToken = default);
}


