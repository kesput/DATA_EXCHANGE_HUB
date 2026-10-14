using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IArgusServerMetricsService
{
    Task<ArgusServerMetricsDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default);
}
