using System.Data;
using System.Globalization;
using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class OraculService(
    IConfiguration configuration,
    IOptions<OraculOptions> options,
    IUserNameResolver userNameResolver) : IOraculService
{
    private readonly OraculOptions _options = options.Value;

    public async Task<IReadOnlyList<string>> GetReportsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var sourceTableName = SqlName.NormalizeObjectName(
            _options.ReportSourceTableName,
            "BLSTXSTAT1.BLS_LOGIT_STAT.dbo.LSP_ORDERS_BY_WEEKDAY_POS");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT DISTINCT LTRIM(RTRIM([REPORTNAME]))
            FROM {sourceTableName}
            WHERE [REPORTNAME] IS NOT NULL
                AND LTRIM(RTRIM([REPORTNAME])) <> N''
            ORDER BY LTRIM(RTRIM([REPORTNAME]))
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reports = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                reports.Add(reader.GetString(0));
            }
        }

        return reports
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    public async Task<OraculQueryResult> ExecuteAsync(
        int? dayNumber,
        DateTime? selectedDate,
        string reportName,
        CancellationToken cancellationToken = default)
    {
        if (dayNumber.HasValue && dayNumber.Value is < 1 or > 7)
        {
            throw new InvalidOperationException("Savaites diena turi buti nuo 1 iki 7.");
        }

        if (!dayNumber.HasValue && !selectedDate.HasValue)
        {
            throw new InvalidOperationException("Nurodykite savaites diena arba data.");
        }

        var reports = await GetReportsAsync(cancellationToken);
        if (!reports.Contains(reportName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Pasirinktas netinkamas KPI.");
        }

        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.DataProcedureName,
            "SNTXDATA.dbo.LSP_ORDERS_BY_WEEKDAY");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = $"EXEC {procedureName} @WEEKDAY = @Weekday, @DATE = @Date, @REPORTNAME = @ReportName";
        command.Parameters.Add("@Weekday", SqlDbType.Int).Value = dayNumber.HasValue
            ? dayNumber.Value
            : DBNull.Value;
        command.Parameters.Add("@Date", SqlDbType.Date).Value = selectedDate.HasValue
            ? selectedDate.Value.Date
            : DBNull.Value;
        command.Parameters.Add("@ReportName", SqlDbType.NVarChar, 100).Value = reportName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Enumerable
            .Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                var rawValue = reader[column];
                values[column] = rawValue == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(rawValue, CultureInfo.CurrentCulture);
            }

            rows.Add(values);
        }

        return new OraculQueryResult(columns, rows);
    }

    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }
}
