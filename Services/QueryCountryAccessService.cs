using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace DATA_EXCHANGE_HUB.Services;

public interface IQueryCountryAccessService
{
    Task<IReadOnlySet<string>> GetUserCountriesAsync(
        ClaimsPrincipal user,
        string appName,
        string connectionStringName,
        CancellationToken cancellationToken = default);

    Task<bool> HasAdminAccessAsync(
        ClaimsPrincipal user,
        string appName,
        string connectionStringName,
        CancellationToken cancellationToken = default);
}

public sealed class QueryCountryAccessService(
    IConfiguration configuration,
    IUserNameResolver userNameResolver) : IQueryCountryAccessService
{
    private static readonly string[] BalticCountries = ["LT", "LV", "EE"];
    private const string AccessTableName = "BLS_LOGIT_STAT.dbo.BS_APP_ACCESS";

    public async Task<IReadOnlySet<string>> GetUserCountriesAsync(
        ClaimsPrincipal user,
        string appName,
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var shortUserName = NormalizeShortUserName(userName);
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $"""
            SELECT COUNTRY
            FROM {AccessTableName}
            WHERE APP_NAME = @AppName
              AND (DOMEN_USER = @UserName OR DOMEN_USER = @ShortUserName)
              AND UPPER([ACCESS]) IN (N'USER', N'ADMIN')
              AND (ACTIVE = 1 OR ACTIVE IS NULL)
              AND COUNTRY IS NOT NULL
            """;
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = appName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;
        command.Parameters.Add("@ShortUserName", SqlDbType.NVarChar, 256).Value = shortUserName;

        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AddCountries(countries, reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return countries;
    }

    public async Task<bool> HasAdminAccessAsync(
        ClaimsPrincipal user,
        string appName,
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var shortUserName = NormalizeShortUserName(userName);
        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $"""
            SELECT TOP (1) 1
            FROM {AccessTableName}
            WHERE APP_NAME = @AppName
              AND (DOMEN_USER = @UserName OR DOMEN_USER = @ShortUserName)
              AND UPPER([ACCESS]) = N'ADMIN'
              AND (ACTIVE = 1 OR ACTIVE IS NULL)
            """;
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = appName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;
        command.Parameters.Add("@ShortUserName", SqlDbType.NVarChar, 256).Value = shortUserName;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && result != DBNull.Value;
    }

    private static void AddCountries(HashSet<string> countries, string rawCountry)
    {
        var country = rawCountry.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(country))
        {
            return;
        }

        if (country.Equals("BS", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var balticCountry in BalticCountries)
            {
                countries.Add(balticCountry);
            }

            return;
        }

        countries.Add(country);
    }

    private static string NormalizeShortUserName(string userName)
    {
        var slashIndex = userName.LastIndexOf('\\');
        return slashIndex >= 0 && slashIndex + 1 < userName.Length
            ? userName[(slashIndex + 1)..]
            : userName;
    }
}
