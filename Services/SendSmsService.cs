using System.Data;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class SendSmsService(
    IConfiguration configuration,
    IOptions<SendSmsOptions> options) : ISendSmsService
{
    private readonly SendSmsOptions _options = options.Value;

    public async Task SendAsync(
        SendSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        var to = NormalizeRequired(request.To, 50, "Iveskite telefono numeri SMS siuntimui.");
        if (!Regex.IsMatch(to, @"^\+370-?\d{8}$"))
        {
            throw new InvalidOperationException("SMS galima siusti tik i lietuviskus numerius, pvz. +37061234567.");
        }

        var text = NormalizeRequired(request.Text, 160, "Iveskite SMS teksta.");

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = SqlName.NormalizeObjectName(_options.ProcedureName, "WM0.SNTXDB.dbo.SEND_SMS");
        command.CommandTimeout = Math.Clamp(_options.CommandTimeoutSeconds, 1, 300);
        command.Parameters.Add("@TO", SqlDbType.NVarChar, 50).Value = to;
        command.Parameters.Add("@TXT", SqlDbType.NVarChar, 160).Value = text;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string GetConnectionString()
    {
        return configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.ConnectionStringName}' nesukonfiguruota.");
    }

    private static string NormalizeRequired(string? value, int maxLength, string errorMessage)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}



