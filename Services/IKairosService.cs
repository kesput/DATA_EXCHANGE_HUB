using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IKairosService
{
    Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    DateTime GetMinimumNeedDate();

    bool IsLocked(DateTime needDate);

    Task<IReadOnlyList<KairosRequest>> ListRequestsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KairosLogEntry>> ListLogsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KairosPromoForecast>> ListPromoForecastAsync(
        CancellationToken cancellationToken = default);

    Task<KairosPromoForecastImportResult> ImportPromoForecastAsync(
        ExcelWorksheetData worksheet,
        string actorUserName,
        CancellationToken cancellationToken = default);
    Task<int> CreateRequestAsync(
        KairosCreateRequest request,
        string actorUserName,
        CancellationToken cancellationToken = default);
    Task DeletePromoForecastAsync(
        int id,
        string actorUserName,
        CancellationToken cancellationToken = default);
    Task DeleteRequestAsync(
        int requestId,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task RestoreDeletedRequestAsync(
        long logId,
        string actorUserName,
        CancellationToken cancellationToken = default);
}







