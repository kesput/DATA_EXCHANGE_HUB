using System.Data;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HubFeedbackService(
    IConfiguration configuration,
    IOptions<WorkHubOptions> options) : IHubFeedbackService
{
    private readonly WorkHubOptions _options = options.Value;

    private string SuggestionTable => SqlName.NormalizeTableName(
        _options.FeedbackSuggestionTableName,
        "BLS_LOGIT_STAT.dbo.HUB_SUGGESTIONS");

    public async Task SaveSuggestionAsync(
        HubSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new InvalidOperationException("Pasiulymo tekstas tuscias.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {SuggestionTable} (USER_NAME, MESSAGE, CREATED_AT, STATUS)
            VALUES (@UserName, @Message, SYSDATETIMEOFFSET(), N'NEW');
            """;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value =
            string.IsNullOrWhiteSpace(request.UserName) ? "anonymous" : request.UserName.Trim();
        command.Parameters.Add("@Message", SqlDbType.NVarChar, 2000).Value = request.Message.Trim();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(_options.FeedbackConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.FeedbackConnectionStringName}' nesukonfiguruota.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

}
