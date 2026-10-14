using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HubSecurityAlertService(
    IOptions<SendMailOptions> sendMailOptions,
    ISendMailService sendMailService,
    ILogger<HubSecurityAlertService> logger) : IHubSecurityAlertService
{
    private readonly SendMailOptions _sendMailOptions = sendMailOptions.Value;

    public async Task NotifySuspiciousLoginAsync(
        string activeUserName,
        string attemptedUserName,
        string remoteAddress,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        var recipient = string.IsNullOrWhiteSpace(_sendMailOptions.SecurityAlertRecipient)
            ? _sendMailOptions.TestAccessRequestRecipient
            : _sendMailOptions.SecurityAlertRecipient;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning(
                "Security alert recipient is not configured. Active user {ActiveUser} attempted login {AttemptedUser} at {OccurredAt} from {RemoteAddress}.",
                activeUserName,
                attemptedUserName,
                occurredAt,
                remoteAddress);
            return;
        }

        var body = $"""
            Data Exchange Hub aptiko itartina prisijungimo bandyma.

            Laikas: {occurredAt:yyyy-MM-dd HH:mm:ss zzz}
            Aktyvus / atpazintas vartotojas: {activeUserName}
            Bandytas prisijungimo vardas: {attemptedUserName}
            IP adresas: {(string.IsNullOrWhiteSpace(remoteAddress) ? "-" : remoteAddress)}

            Slaptazodis pranesime nera saugomas ir nesiunciamas.
            """;

        var draft = sendMailService.BuildDraft(
            "HUB_SECURITY",
            "Suspicious login attempt",
            recipient,
            "Kestutis",
            body,
            string.IsNullOrWhiteSpace(activeUserName) ? "SYSTEM" : activeUserName);

        await sendMailService.SendAsync(draft, cancellationToken);
    }
}