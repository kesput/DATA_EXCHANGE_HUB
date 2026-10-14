using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IIrisService
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IrisReportSummary>> ListReportsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IrisReportSource>> ListSourcesAsync(long reportId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IrisReportRecipient>> ListRecipientsAsync(long reportId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IrisSchedule>> ListSchedulesAsync(long reportId, CancellationToken cancellationToken);

    Task<long> CreateReportAsync(IrisCreateReportRequest request, CancellationToken cancellationToken);

    Task UpdateReportAsync(IrisUpdateReportRequest request, CancellationToken cancellationToken);

    Task<long> CreateSourceAsync(IrisCreateSourceRequest request, CancellationToken cancellationToken);

    Task UpdateSourceAsync(IrisUpdateSourceRequest request, CancellationToken cancellationToken);

    Task DeleteSourceAsync(long sourceId, CancellationToken cancellationToken);

    Task<long> CreateRecipientAsync(IrisCreateRecipientRequest request, CancellationToken cancellationToken);

    Task SetRecipientActiveAsync(long recipientId, bool isActive, CancellationToken cancellationToken);

    Task<long> CreateScheduleAsync(IrisCreateScheduleRequest request, CancellationToken cancellationToken);

    Task UpdateScheduleAsync(IrisUpdateScheduleRequest request, CancellationToken cancellationToken);

    Task SetScheduleActiveAsync(long scheduleId, bool isActive, CancellationToken cancellationToken);

    Task DeleteScheduleAsync(long scheduleId, CancellationToken cancellationToken);
}
