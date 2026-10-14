using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface ISeshatService
{
    Task<SeshatAccessState> GetAccessAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeshatDocumentSummary>> ListDocumentsAsync(
        SeshatAccessState access,
        string? search,
        string? status,
        string? country,
        CancellationToken cancellationToken = default);

    Task<SeshatDocumentDetails?> GetDocumentDetailsAsync(
        int documentId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task<int> SaveDocumentAsync(
        int? documentId,
        string title,
        string description,
        string keywords,
        string status,
        DateTime? validFrom,
        DateTime? validTo,
        string countriesText,
        int? reminderDaysBefore,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task UploadFileAsync(
        int documentId,
        IFormFile file,
        string description,
        int? sectionId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task UploadFilesAsync(
        int documentId,
        IReadOnlyList<IFormFile> files,
        string description,
        int? sectionId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);
    Task<int> CreateSectionAsync(
        int documentId,
        string title,
        string body,
        IReadOnlyList<IFormFile> files,
        string fileDescription,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task UpdateSectionAsync(
        int sectionId,
        string title,
        string body,
        IReadOnlyList<IFormFile> files,
        string fileDescription,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);


    Task SaveReminderAsync(
        int documentId,
        string notifyChannel,
        string contactValue,
        int daysBefore,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task CancelReminderAsync(
        int reminderId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task SendReminderTestAsync(
        int documentId,
        int? reminderId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task<int> SendDueRemindersAsync(
        DateTime sendDate,
        CancellationToken cancellationToken = default);
    Task<SeshatFileEntry?> GetFileAsync(
        int fileId,
        SeshatAccessState access,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    string GetPhysicalFilePath(SeshatFileEntry file);

    Task DeleteFileAsync(
        int fileId,
        string deleteReason,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);

    Task RestoreFileAsync(
        int fileId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default);
}

