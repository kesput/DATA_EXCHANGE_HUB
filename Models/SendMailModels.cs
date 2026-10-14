namespace DATA_EXCHANGE_HUB.Models;

public sealed record SendMailDraft(
    string ApplicationName,
    string ProcessName,
    string RecipientEmail,
    string RecipientName,
    string Subject,
    string BodyHtml,
    string CreatedBy,
    string SenderEmail = "",
    string SenderDisplayName = "",
    string SenderKey = "");

public sealed record SendMailArchiveResult(
    long? MailId,
    string Subject,
    string ArchivePath,
    DateTimeOffset CreatedAt);

public sealed record SendMailLogEntry(
    long Id,
    string ApplicationName,
    string ProcessName,
    string RecipientEmail,
    string Subject,
    string Status,
    string ArchivePath,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record SendMailSenderProfile(
    string Key,
    string DisplayName,
    string Email,
    string SignatureKind);
