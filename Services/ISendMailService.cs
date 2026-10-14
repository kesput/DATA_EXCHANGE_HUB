using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface ISendMailService
{
    SendMailDraft BuildDraft(
        string applicationName,
        string processName,
        string recipientEmail,
        string recipientName,
        string message,
        string createdBy);

    SendMailDraft BuildDraft(
        string applicationName,
        string processName,
        string recipientEmail,
        string recipientName,
        string message,
        string createdBy,
        string senderKey);

    IReadOnlyList<SendMailSenderProfile> ListSenderProfiles();

    Task<SendMailArchiveResult> SaveDraftAsync(
        SendMailDraft draft,
        CancellationToken cancellationToken = default);

    Task<SendMailArchiveResult> SendAsync(
        SendMailDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SendMailLogEntry>> ListRecentAsync(
        CancellationToken cancellationToken = default);

    string GetArchiveRoot();
}
