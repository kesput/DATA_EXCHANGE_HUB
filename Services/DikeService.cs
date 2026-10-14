using System.Data;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class DikeService(
    IConfiguration configuration,
    IOptions<DikeOptions> options) : IDikeService
{
    private readonly DikeOptions _options = options.Value;

    public async Task<IReadOnlyList<DikeClientInfo>> ListClientsAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var clients = new List<DikeClientInfo>();
        await using var connection = new SqlConnection(GetConnectionString(_options.Stat3ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("""
            SELECT
                [OWNER].[ID],
                [OWNER].[FIRMID],
                [OWNER].[SID],
                [OWNER].[SHORTNAME],
                [OWNER].[NAME],
                [OWNER].[REGCODE],
                ORGALL.[ADDRESS]
            FROM [SNTXDATA].[dbo].[OWNER] AS [OWNER] WITH (NOLOCK)
            LEFT JOIN [SNTXDATA].dbo.ORGALL AS ORGALL WITH (NOLOCK)
                ON ORGALL.FIRMID = OWNER.FIRMID
               AND ORGALL.ORGID = OWNER.ORGID
            WHERE [OWNER].ACTIVE = 1
              AND [OWNER].FIRMID = N'SA'
              AND [OWNER].[SID] = N'BLT'
              AND (
                    @Search = N''
                    OR CONVERT(NVARCHAR(30), [OWNER].[ID]) = @Search
                    OR [OWNER].[SHORTNAME] LIKE @SearchLike
                    OR [OWNER].[NAME] LIKE @SearchLike
                    OR [OWNER].[REGCODE] LIKE @SearchLike
                  )
            ORDER BY [OWNER].[SHORTNAME], [OWNER].[NAME], [OWNER].[ID];
            """, connection);
        AddSearchParameters(command, searchTerm);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            clients.Add(ReadClient(reader));
        }

        return clients;
    }

    public async Task<DikeClientInfo?> GetClientAsync(
        long ownerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString(_options.Stat3ConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("""
            SELECT
                [OWNER].[ID],
                [OWNER].[FIRMID],
                [OWNER].[SID],
                [OWNER].[SHORTNAME],
                [OWNER].[NAME],
                [OWNER].[REGCODE],
                ORGALL.[ADDRESS]
            FROM [SNTXDATA].[dbo].[OWNER] AS [OWNER] WITH (NOLOCK)
            LEFT JOIN [SNTXDATA].dbo.ORGALL AS ORGALL WITH (NOLOCK)
                ON ORGALL.FIRMID = OWNER.FIRMID
               AND ORGALL.ORGID = OWNER.ORGID
            WHERE [OWNER].ACTIVE = 1
              AND [OWNER].FIRMID = N'SA'
              AND [OWNER].[SID] = N'BLT'
              AND [OWNER].[ID] = @OwnerId;
            """, connection);
        command.Parameters.Add("@OwnerId", SqlDbType.BigInt).Value = ownerId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadClient(reader)
            : null;
    }

    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }

    private static DikeClientInfo ReadClient(SqlDataReader reader) =>
        new(
            Convert.ToInt64(reader.GetValue(0)),
            ReadString(reader, 1),
            ReadString(reader, 2),
            ReadString(reader, 3),
            ReadString(reader, 4),
            ReadString(reader, 5),
            ReadString(reader, 6));

    private static string ReadString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static void AddSearchParameters(SqlCommand command, string searchTerm)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(searchTerm) ? string.Empty : searchTerm.Trim();
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = normalizedSearch;
        command.Parameters.Add("@SearchLike", SqlDbType.NVarChar, 240).Value = $"%{EscapeLikeValue(normalizedSearch)}%";
    }

    private static string EscapeLikeValue(string value) =>
        value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
}
