using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed partial class SendMailService(
    IConfiguration configuration,
    IOptions<SendMailOptions> options,
    IWebHostEnvironment environment) : ISendMailService
{
    private const string DefaultLogTableName = "BLS_LOGIT_STAT.dbo.HUB_SEND_MAIL_LOG";
    private const string PreviewMarker = "DataExchaneHub_PREVIEW";
    private readonly SendMailOptions _options = options.Value;

    public SendMailDraft BuildDraft(
        string applicationName,
        string processName,
        string recipientEmail,
        string recipientName,
        string message,
        string createdBy)
    {
        return BuildDraft(
            applicationName,
            processName,
            recipientEmail,
            recipientName,
            message,
            createdBy,
            string.Empty);
    }

    public SendMailDraft BuildDraft(
        string applicationName,
        string processName,
        string recipientEmail,
        string recipientName,
        string message,
        string createdBy,
        string senderKey)
    {
        var app = Normalize(applicationName, 80, "HUB");
        var process = Normalize(processName, 160, "Info");
        var email = Normalize(recipientEmail, 320, string.Empty);
        var name = Normalize(recipientName, 160, string.Empty);
        var bodyMessage = NormalizeMultiline(message, 4000);
        var user = Normalize(createdBy, 256, "SYSTEM");
        var sender = ResolveSenderProfile(senderKey);

        if (!EmailRegex().IsMatch(email))
        {
            throw new InvalidOperationException("Iveskite korektiska gavejo el. pasto adresa.");
        }

        var subject = BuildSubject();
        var html = BuildHtml(app, process, name, bodyMessage, sender);
        return new SendMailDraft(app, process, email, name, subject, html, user, sender.Email, sender.DisplayName, sender.Key);
    }

    public IReadOnlyList<SendMailSenderProfile> ListSenderProfiles()
    {
        return BuildSenderProfiles();
    }

    public Task<SendMailArchiveResult> SaveDraftAsync(
        SendMailDraft draft,
        CancellationToken cancellationToken = default)
    {
        return PersistAsync(draft, "DRAFT_SAVED", send: false, cancellationToken);
    }

    public Task<SendMailArchiveResult> SendAsync(
        SendMailDraft draft,
        CancellationToken cancellationToken = default)
    {
        return PersistAsync(draft, "QUEUED", send: true, cancellationToken);
    }

    public async Task<IReadOnlyList<SendMailLogEntry>> ListRecentAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (@Limit)
                    ID,
                    APPLICATION_NAME,
                    PROCESS_NAME,
                    RECIPIENT_EMAIL,
                    SUBJECT,
                    STATUS,
                    ARCHIVE_PATH,
                    CREATED_BY,
                    CREATED_AT
                FROM {LogTableName()}
                ORDER BY CREATED_AT DESC, ID DESC
                """;
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(_options.RecentLimit, 1, 200);

            var rows = new List<SendMailLogEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SendMailLogEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetFieldValue<DateTimeOffset>(8)));
            }

            return rows;
        }
        catch
        {
            return [];
        }
    }

    public string GetArchiveRoot()
    {
        var configuredPath = environment.IsDevelopment()
            ? _options.LocalArchivePath
            : _options.IisArchivePath;

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    private async Task<SendMailArchiveResult> PersistAsync(
        SendMailDraft draft,
        string initialStatus,
        bool send,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.Now;
        var fileName = BuildSafeFileName($"{createdAt:HHmmss}_{draft.ApplicationName}_{draft.ProcessName}_{draft.RecipientEmail}.html");
        var archivePath = Path.Combine(GetArchiveRoot(), createdAt.ToString("yyyy"), createdAt.ToString("MM"), createdAt.ToString("dd"), fileName);
        var mailId = await TryInsertLogAsync(draft, archivePath, createdAt, initialStatus, cancellationToken);
        var footerMarker = $"DataExchaneHub_{(mailId.HasValue ? mailId.Value.ToString() : "NOLOG")}";
        var finalBody = draft.BodyHtml.Replace(BuildFooter(PreviewMarker), BuildFooter(footerMarker));

        var archived = await TryWriteArchiveAsync(archivePath, finalBody, cancellationToken);
        if (mailId.HasValue)
        {
            await TryUpdateLogAsync(
                mailId.Value,
                archived ? "ARCHIVED" : "ARCHIVE_FAILED",
                finalBody,
                archived ? null : $"Nepavyko irasyti laisko archyvo: {archivePath}",
                cancellationToken);
        }

        if (!send)
        {
            return new SendMailArchiveResult(mailId, draft.Subject, archivePath, createdAt);
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(
                    string.IsNullOrWhiteSpace(draft.SenderEmail) ? _options.FromEmail : draft.SenderEmail,
                    string.IsNullOrWhiteSpace(draft.SenderDisplayName) ? _options.FromDisplayName : draft.SenderDisplayName),
                Subject = draft.Subject,
                Body = finalBody,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            message.To.Add(new MailAddress(draft.RecipientEmail, draft.RecipientName));

            using var smtp = new SmtpClient(_options.SmtpServer, _options.SmtpPort)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                EnableSsl = _options.EnableSsl,
                Timeout = Math.Max(1, _options.SmtpTimeoutSeconds) * 1000
            };
            if (!string.IsNullOrWhiteSpace(_options.SmtpUserName))
            {
                smtp.Credentials = new NetworkCredential(
                    _options.SmtpUserName,
                    _options.SmtpPassword);
            }

            await smtp.SendMailAsync(message, cancellationToken);
            if (mailId.HasValue)
            {
                await TryUpdateLogAsync(mailId.Value, "SENT", finalBody, null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (mailId.HasValue)
            {
                await TryUpdateLogAsync(mailId.Value, "SEND_FAILED", finalBody, ex.Message, cancellationToken);
            }

            throw;
        }

        return new SendMailArchiveResult(mailId, draft.Subject, archivePath, createdAt);
    }

    private static async Task<bool> TryWriteArchiveAsync(
        string archivePath,
        string bodyHtml,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(archivePath, bodyHtml, Encoding.UTF8, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private async Task<long?> TryInsertLogAsync(
        SendMailDraft draft,
        string archivePath,
        DateTimeOffset createdAt,
        string status,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {LogTableName()}
                (
                    APPLICATION_NAME,
                    PROCESS_NAME,
                    RECIPIENT_EMAIL,
                    RECIPIENT_NAME,
                    SUBJECT,
                    BODY_HTML,
                    STATUS,
                    ARCHIVE_PATH,
                    CREATED_BY,
                    CREATED_AT
                )
                OUTPUT INSERTED.ID
                VALUES
                (
                    @ApplicationName,
                    @ProcessName,
                    @RecipientEmail,
                    @RecipientName,
                    @Subject,
                    @BodyHtml,
                    @Status,
                    @ArchivePath,
                    @CreatedBy,
                    @CreatedAt
                )
                """;
            command.Parameters.Add("@ApplicationName", SqlDbType.NVarChar, 80).Value = draft.ApplicationName;
            command.Parameters.Add("@ProcessName", SqlDbType.NVarChar, 160).Value = draft.ProcessName;
            command.Parameters.Add("@RecipientEmail", SqlDbType.NVarChar, 320).Value = draft.RecipientEmail;
            command.Parameters.Add("@RecipientName", SqlDbType.NVarChar, 160).Value = draft.RecipientName;
            command.Parameters.Add("@Subject", SqlDbType.NVarChar, 300).Value = draft.Subject;
            command.Parameters.Add("@BodyHtml", SqlDbType.NVarChar, -1).Value = draft.BodyHtml;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 40).Value = status;
            command.Parameters.Add("@ArchivePath", SqlDbType.NVarChar, 1000).Value = archivePath;
            command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 256).Value = draft.CreatedBy;
            command.Parameters.Add("@CreatedAt", SqlDbType.DateTimeOffset).Value = createdAt;
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        catch
        {
            return null;
        }
    }

    private async Task TryUpdateLogAsync(
        long mailId,
        string status,
        string bodyHtml,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE {LogTableName()}
                SET STATUS = @Status,
                    BODY_HTML = @BodyHtml,
                    ERROR_MESSAGE = @ErrorMessage,
                    SENT_AT = CASE WHEN @Status = N'SENT' THEN SYSDATETIMEOFFSET() ELSE SENT_AT END
                WHERE ID = @Id
                """;
            command.Parameters.Add("@Id", SqlDbType.BigInt).Value = mailId;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 40).Value = status;
            command.Parameters.Add("@BodyHtml", SqlDbType.NVarChar, -1).Value = bodyHtml;
            command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2000).Value =
                string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private string BuildHtml(string applicationName, string processName, string recipientName, string message, SendMailSenderProfile sender)
    {
        var greetingName = string.IsNullOrWhiteSpace(recipientName) ? "kolega" : WebUtility.HtmlEncode(recipientName);
        var encodedMessage = WebUtility.HtmlEncode(message).Replace("\n", "<br>");
        var signatureBody = ReadSignatureBody(sender);
        var platformUrl = WebUtility.HtmlEncode(_options.PlatformUrl);
        return $$"""
            <!doctype html>
            <html lang="lt">
            <head>
                <meta charset="utf-8">
                <title>{{WebUtility.HtmlEncode(BuildSubject())}}</title>
            </head>
            <body style="margin:0;padding:0;background:#d9fae5;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#1f2b20;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#d9fae5;padding:24px 0;">
                    <tr>
                        <td align="center" style="padding:0 12px;">
                            <table role="presentation" width="680" cellspacing="0" cellpadding="0" style="max-width:680px;width:100%;background:#ffffff;border:1px solid #c6e1a6;border-collapse:separate;">
                                <tr>
                                    <td style="padding:0;background:#ff9800;border-bottom:4px solid #388e3c;">
                                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                                            <tr>
                                                <td style="padding:18px 24px;">
                                                    <div style="font-size:12px;font-weight:800;letter-spacing:.08em;text-transform:uppercase;color:#035e21;">Data Exchange Hub</div>
                                                    <div style="margin-top:5px;font-size:22px;line-height:1.25;font-weight:800;color:#16351f;">{{WebUtility.HtmlEncode(processName)}}</div>
                                                    <div style="margin-top:7px;font-size:13px;font-weight:700;color:#24492b;">{{WebUtility.HtmlEncode(applicationName)}} · {{WebUtility.HtmlEncode(BuildSubject())}}</div>
                                                </td>
                                                <td align="right" style="width:96px;padding:18px 24px 18px 0;">
                                                    <div style="display:inline-block;width:56px;height:56px;line-height:56px;text-align:center;background:#388e3c;color:#ffffff;font-size:18px;font-weight:800;border-radius:8px;">HUB</div>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                                <tr>
                                    <td style="padding:28px;background:#ffffff;">
                                        <p style="margin:0 0 16px;font-size:15px;line-height:1.6;color:#1f2b20;">Sveiki, {{greetingName}},</p>
                                        <div style="margin:0 0 22px;padding:16px 18px;background:#dff5c9;border-left:5px solid #ff9800;color:#24492b;font-size:15px;line-height:1.6;">
                                            {{encodedMessage}}
                                        </div>

                                        <div style="margin:20px 0 0;padding:14px 16px;background:#f4f7f2;border:1px solid #c6e1a6;color:#38553d;font-size:13px;line-height:1.5;">
                                            Pranesimu prenumeratas galite uzsisakyti arba atsaukti web platformoje:<br>
                                            <a href="{{platformUrl}}" style="color:#035e21;font-weight:700;text-decoration:underline;">{{platformUrl}}</a>
                                        </div>
                                        {{signatureBody}}
                                        {{BuildFooter(PreviewMarker)}}
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
            </body>
            </html>
            """;
    }
    private static string BuildSubject()
    {
        return $"DataExchaneHub Info {DateTime.Today:yyyy-MM-dd}";
    }

    private string ReadSignatureBody(SendMailSenderProfile sender)
    {
        var path = environment.IsDevelopment()
            ? _options.LocalSignaturePath
            : _options.IisSignaturePath;
        if (sender.SignatureKind.Equals("bls", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path, Encoding.UTF8);
                }
            }
            catch
            {
                // Signature is decorative; mail sending should not fail if IIS cannot read it.
            }
        }

        if (sender.SignatureKind.Equals("sanitex", StringComparison.OrdinalIgnoreCase))
        {
            return """
                <div style="margin:24px 0 0;padding-top:16px;border-top:1px solid #d8e1d4;font-family:Arial,sans-serif;color:#1f2b20;">
                    <div style="font-size:16px;font-weight:800;color:#0b5f2a;">SANITEX</div>
                    <div style="margin-top:4px;font-size:13px;line-height:1.5;color:#415041;">
                        Statistics Team<br>
                        <a href="mailto:statistics@sanitex.eu" style="color:#0b5f2a;text-decoration:underline;">statistics@sanitex.eu</a>
                    </div>
                </div>
                """;
        }

        return "<p style=\"margin:24px 0 0;font-size:14px;line-height:1.6;\">Pagarbiai,<br><strong>Data Exchange Hub</strong></p>";
    }
    private static string BuildFooter(string marker)
    {
        return $"""
            <div style="margin-top:18px;">
                <span style="font-size:8.0pt;font-family:Arial,sans-serif;color:#AAAAAA;">Report sended from {WebUtility.HtmlEncode(marker)}</span>
            </div>
            """;
    }


    private SendMailSenderProfile ResolveSenderProfile(string? senderKey)
    {
        var profiles = BuildSenderProfiles();
        return profiles.FirstOrDefault(profile => profile.Key.Equals(senderKey, StringComparison.OrdinalIgnoreCase))
            ?? profiles.First();
    }

    private IReadOnlyList<SendMailSenderProfile> BuildSenderProfiles()
    {
        var configuredProfiles = _options.SenderProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Email))
            .Select(profile => new SendMailSenderProfile(
                Normalize(profile.Key, 40, profile.Email),
                Normalize(profile.DisplayName, 120, _options.FromDisplayName),
                Normalize(profile.Email, 320, _options.FromEmail),
                Normalize(profile.SignatureKind, 40, "bls")))
            .ToArray();

        return configuredProfiles.Length > 0
            ? configuredProfiles
            :
            [
                new SendMailSenderProfile("default", _options.FromDisplayName, _options.FromEmail, "bls")
            ];
    }
    private string GetConnectionString()
    {
        return configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.ConnectionStringName}' nesukonfiguruota.");
    }

    private string LogTableName() => SqlName.NormalizeTableName(_options.LogTableName, DefaultLogTableName);

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string NormalizeMultiline(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "Informuojame apie atnaujinta busena. Detales pateikiamos siame pranesime.";
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string BuildSafeFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalidCharacter, '_');
        }

        return safe;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}









