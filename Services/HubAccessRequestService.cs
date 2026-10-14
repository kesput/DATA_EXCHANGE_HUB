using System.Data;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HubAccessRequestService(
    IConfiguration configuration,
    IOptions<WorkHubOptions> options,
    IOptions<SendMailOptions> sendMailOptions,
    ISendMailService sendMailService,
    ISendSmsService sendSmsService,
    ILogger<HubAccessRequestService> logger) : IHubAccessRequestService
{
    private readonly WorkHubOptions _options = options.Value;
    private readonly SendMailOptions _sendMailOptions = sendMailOptions.Value;

    public async Task SaveAsync(
        HubAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(_options.AccessRequestConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.AccessRequestConnectionStringName}' nesukonfiguruota.");
        var tableName = SqlName.NormalizeTableName(
            _options.AccessRequestTableName,
            "BLS_LOGIT_STAT.dbo.HUB_ACCESS_REQUESTS");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {tableName}
            (
                DOMEN_USER,
                APP_NAME,
                REQUESTED_ACCESS,
                COMMENT,
                STATUS,
                REQUESTED_AT,
                REQUESTED_BY
            )
            VALUES
            (
                @DomainUser,
                @AppName,
                @RequestedAccess,
                @Comment,
                N'NEW',
                SYSDATETIMEOFFSET(),
                @DomainUser
            );
            """;
        command.Parameters.Add("@DomainUser", SqlDbType.NVarChar, 256).Value = request.DomainUser;
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = request.AppName;
        command.Parameters.Add("@RequestedAccess", SqlDbType.NVarChar, 50).Value = request.RequestedAccess;
        command.Parameters.Add("@Comment", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(request.Comment)
            ? DBNull.Value
            : request.Comment;

        await command.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            await SendAccessRequestMailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nepavyko issiusti HUB_ACCESS_REQUESTS testinio el. laisko vartotojui {User} del {AppName}.", request.DomainUser, request.AppName);
        }

        try
        {
            await SendAccessRequestSmsAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nepavyko issiusti HUB_ACCESS_REQUESTS SMS vartotojui {User} del {AppName}.", request.DomainUser, request.AppName);
        }
    }

    private async Task SendAccessRequestMailAsync(
        HubAccessRequest request,
        CancellationToken cancellationToken)
    {
        var body = $"""
            Data Exchange Hub uzregistravo nauja prieigos prasyma.

            Modelio irasas: HUB_ACCESS_REQUESTS
            Aplikacija: {request.AppName}
            Vartotojas: {request.DomainUser}
            Prasoma prieiga: {request.RequestedAccess}
            Komentaras: {(string.IsNullOrWhiteSpace(request.Comment) ? "-" : request.Comment)}
            """;

        var draft = sendMailService.BuildDraft(
            "HUB_ACCESS_REQUESTS",
            $"{request.AppName} access request",
            _sendMailOptions.TestAccessRequestRecipient,
            "Kestutis",
            body,
            request.DomainUser);

        await sendMailService.SendAsync(draft, cancellationToken);
    }
    private async Task SendAccessRequestSmsAsync(
        HubAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TestAccessRequestPhone))
        {
            return;
        }

        var text = $"Hub teisiu prasymas: {request.AppName}, {request.DomainUser}, {request.RequestedAccess}.";
        await sendSmsService.SendAsync(
            new SendSmsRequest(
                _options.TestAccessRequestPhone,
                text,
                request.DomainUser,
                "HUB_ACCESS_REQUESTS"),
            cancellationToken);
    }
}

