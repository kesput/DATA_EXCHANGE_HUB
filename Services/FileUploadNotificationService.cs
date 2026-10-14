using System.Data;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class FileUploadNotificationService(
    IConfiguration configuration,
    IOptions<FileUploadOptions> options,
    ISendMailService sendMailService,
    ISendSmsService sendSmsService,
    ILogger<FileUploadNotificationService> logger) : IFileUploadNotificationService
{
    private const string DefaultTableName = "BLS_LOGIT_STAT.dbo.FILE_UPLOAD_NOTIFICATIONS";
    private readonly FileUploadOptions _options = options.Value;

    public async Task<IReadOnlyList<FileUploadNotificationSubscription>> ListForUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (50)
                ID,
                ACTOR_USER,
                FILE_NAME_PATTERN,
                TRIGGER_TYPE,
                NOTIFY_CHANNEL,
                EMAIL,
                PHONE_NO,
                ACTIVE,
                CREATED_AT,
                UPDATE_TIME
            FROM {SubscriptionsTableName()}
            WHERE ACTOR_USER = @ActorUser
            ORDER BY ACTIVE DESC, UPDATE_TIME DESC, ID DESC;
            """;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = NormalizeRequired(userName, 256, "Nepavyko nustatyti vartotojo.");

        var rows = new List<FileUploadNotificationSubscription>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FileUploadNotificationSubscription(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetDateTimeOffset(8),
                reader.GetDateTimeOffset(9)));
        }

        return rows;
    }

    public async Task SaveSubscriptionAsync(
        string userName,
        string fileNamePattern,
        string triggerType,
        string notifyChannel,
        string contact,
        CancellationToken cancellationToken = default)
    {
        var actor = NormalizeRequired(userName, 256, "Nepavyko nustatyti vartotojo.");
        var pattern = NormalizePattern(fileNamePattern);
        var trigger = NormalizeTriggerType(triggerType);
        var channel = NormalizeChannel(notifyChannel);
        var normalizedContact = NormalizeRequired(contact, channel == "EMAIL" ? 320 : 50, channel == "EMAIL" ? "Iveskite el. pasta." : "Iveskite telefono numeri.");
        ValidateContact(channel, normalizedContact);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (
                SELECT 1
                FROM {SubscriptionsTableName()}
                WHERE ACTOR_USER = @ActorUser
                  AND FILE_NAME_PATTERN = @FileNamePattern
                  AND TRIGGER_TYPE = @TriggerType
                  AND NOTIFY_CHANNEL = @NotifyChannel
            )
            BEGIN
                UPDATE {SubscriptionsTableName()}
                SET EMAIL = @Email,
                    PHONE_NO = @PhoneNo,
                    ACTIVE = 1,
                    UPDATE_TIME = SYSDATETIMEOFFSET()
                WHERE ACTOR_USER = @ActorUser
                  AND FILE_NAME_PATTERN = @FileNamePattern
                  AND TRIGGER_TYPE = @TriggerType
                  AND NOTIFY_CHANNEL = @NotifyChannel;
            END
            ELSE
            BEGIN
                INSERT INTO {SubscriptionsTableName()}
                (
                    ACTOR_USER,
                    FILE_NAME_PATTERN,
                    TRIGGER_TYPE,
                    NOTIFY_CHANNEL,
                    EMAIL,
                    PHONE_NO,
                    ACTIVE,
                    CREATED_AT,
                    UPDATE_TIME
                )
                VALUES
                (
                    @ActorUser,
                    @FileNamePattern,
                    @TriggerType,
                    @NotifyChannel,
                    @Email,
                    @PhoneNo,
                    1,
                    SYSDATETIMEOFFSET(),
                    SYSDATETIMEOFFSET()
                );
            END
            """;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = actor;
        command.Parameters.Add("@FileNamePattern", SqlDbType.NVarChar, 260).Value = pattern;
        command.Parameters.Add("@TriggerType", SqlDbType.NVarChar, 20).Value = trigger;
        command.Parameters.Add("@NotifyChannel", SqlDbType.NVarChar, 20).Value = channel;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 320).Value = channel == "EMAIL" ? normalizedContact : string.Empty;
        command.Parameters.Add("@PhoneNo", SqlDbType.NVarChar, 50).Value = channel == "SMS" ? normalizedContact : string.Empty;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CancelSubscriptionAsync(
        int subscriptionId,
        string userName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {SubscriptionsTableName()}
            SET ACTIVE = 0,
                UPDATE_TIME = SYSDATETIMEOFFSET()
            WHERE ID = @Id
              AND ACTOR_USER = @ActorUser;
            """;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = subscriptionId;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = NormalizeRequired(userName, 256, "Nepavyko nustatyti vartotojo.");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task NotifyAsync(
        FileUploadNotificationEvent uploadEvent,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FileUploadNotificationSubscription> subscriptions;
        try
        {
            subscriptions = await LoadMatchingSubscriptionsAsync(uploadEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nepavyko nuskaityti FILE_UPLOAD pranesimu prenumeratu.");
            return;
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                if (subscription.NotifyChannel.Equals("SMS", StringComparison.OrdinalIgnoreCase))
                {
                    await sendSmsService.SendAsync(
                        BuildSms(uploadEvent, subscription),
                        cancellationToken);
                }
                else
                {
                    await sendMailService.SendAsync(
                        BuildMail(uploadEvent, subscription),
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nepavyko issiusti FILE_UPLOAD pranesimo prenumeratai {SubscriptionId}.", subscription.Id);
            }
        }
    }

    private async Task<IReadOnlyList<FileUploadNotificationSubscription>> LoadMatchingSubscriptionsAsync(
        FileUploadNotificationEvent uploadEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ID,
                ACTOR_USER,
                FILE_NAME_PATTERN,
                TRIGGER_TYPE,
                NOTIFY_CHANNEL,
                EMAIL,
                PHONE_NO,
                ACTIVE,
                CREATED_AT,
                UPDATE_TIME
            FROM {SubscriptionsTableName()}
            WHERE ACTIVE = 1
              AND @FileName LIKE FILE_NAME_PATTERN
              AND (TRIGGER_TYPE = N'ANY_UPLOAD' OR TRIGGER_TYPE = @TriggerType);
            """;
        command.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = uploadEvent.FileName;
        command.Parameters.Add("@TriggerType", SqlDbType.NVarChar, 20).Value = NormalizeTriggerType(uploadEvent.TriggerType);

        var rows = new List<FileUploadNotificationSubscription>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FileUploadNotificationSubscription(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetDateTimeOffset(8),
                reader.GetDateTimeOffset(9)));
        }

        return rows;
    }

    private SendMailDraft BuildMail(
        FileUploadNotificationEvent uploadEvent,
        FileUploadNotificationSubscription subscription)
    {
        var action = uploadEvent.TriggerType == "REPLACE_UPLOAD" ? "atnaujintas" : "ikeltas";
        var message = $"""
            EXCHANGE_UPLOAD kataloge {action} failas.

            Failas: {uploadEvent.FileName}
            Dydis: {FormatFileSize(uploadEvent.FileSizeBytes)}
            Ikele: {uploadEvent.ActorUser}
            Laikas: {UiFormat.DateTime(uploadEvent.EventAt.LocalDateTime)}
            Taisykle: {subscription.FileNamePattern}
            """;

        return sendMailService.BuildDraft(
            "Data Exchange Hub",
            "EXCHANGE_UPLOAD pranesimas",
            subscription.Email,
            subscription.ActorUser,
            message,
            "FILE_UPLOAD_NOTIFICATION");
    }

    private static SendSmsRequest BuildSms(
        FileUploadNotificationEvent uploadEvent,
        FileUploadNotificationSubscription subscription)
    {
        var action = uploadEvent.TriggerType == "REPLACE_UPLOAD" ? "atnaujintas" : "ikeltas";
        var text = $"EXCHANGE_UPLOAD: {action} {uploadEvent.FileName} ({FormatFileSize(uploadEvent.FileSizeBytes)}).";
        return new SendSmsRequest(
            subscription.PhoneNo,
            text.Length <= 160 ? text : text[..160],
            "FILE_UPLOAD_NOTIFICATION",
            "FILE_UPLOAD_NOTIFY");
    }

    private string GetConnectionString()
    {
        return configuration.GetConnectionString(_options.AccessConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.AccessConnectionStringName}' nesukonfiguruota.");
    }

    private string SubscriptionsTableName() => SqlName.NormalizeTableName(_options.NotificationTableName, DefaultTableName);

    private static string NormalizePattern(string value)
    {
        var pattern = NormalizeRequired(value, 260, "Iveskite failo pavadinimo sablona.");
        var meaningfulLength = pattern.Count(character => character != '%' && character != '_' && !char.IsWhiteSpace(character));
        if (meaningfulLength < 3)
        {
            throw new InvalidOperationException("Sablone turi buti bent 3 konkretus simboliai.");
        }

        return pattern;
    }

    private static string NormalizeTriggerType(string value)
    {
        var trigger = NormalizeRequired(value, 20, "Pasirinkite failo ivyki.").ToUpperInvariant();
        return trigger is "NEW_UPLOAD" or "REPLACE_UPLOAD" or "ANY_UPLOAD"
            ? trigger
            : throw new InvalidOperationException("Pasirinkite failo ivyki.");
    }

    private static string NormalizeChannel(string value)
    {
        var channel = NormalizeRequired(value, 20, "Pasirinkite pranesimo buda.").ToUpperInvariant();
        return channel is "EMAIL" or "SMS" ? channel : throw new InvalidOperationException("Pasirinkite pranesimo buda.");
    }

    private static void ValidateContact(string channel, string contact)
    {
        if (channel == "EMAIL")
        {
            if (!Regex.IsMatch(contact, @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException("Iveskite korektiska el. pasto adresa, pvz. user@bls.lt.");
            }

            return;
        }

        if (!Regex.IsMatch(contact, @"^\+370-?\d{8}$"))
        {
            throw new InvalidOperationException("SMS pranesimams iveskite lietuviska telefono numeri, pvz. +37061234567.");
        }
    }
    private static string NormalizeRequired(string value, int maxLength, string errorMessage)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.##} MB";
        }

        return bytes >= 1024L
            ? $"{bytes / 1024d:0.##} KB"
            : $"{bytes} B";
    }
}

