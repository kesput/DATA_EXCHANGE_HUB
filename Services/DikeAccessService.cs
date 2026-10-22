using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class DikeAccessService(
    IConfiguration configuration,
    IOptions<DikeOptions> options,
    IUserNameResolver userNameResolver) : IDikeAccessService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private readonly DikeOptions _options = options.Value;

    public async Task<bool> IsAdminAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString(_options.AccessConnectionStringName));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {SqlName.NormalizeTableName(_options.AccessProcedureName, DefaultAccessProcedureName)} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userNameResolver.ResolveUserName(user);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            for (var column = 0; column < reader.FieldCount; column++)
                if (new[] { "ACCESS", "ACCESS_LEVEL", "AccessLevel", "ROLE", "RoleName" }.Contains(reader.GetName(column), StringComparer.OrdinalIgnoreCase) &&
                    !reader.IsDBNull(column) && Convert.ToString(reader.GetValue(column))?.Trim().Equals("ADMIN", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
        return false;
    }

    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            DefaultAccessProcedureName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }
}
