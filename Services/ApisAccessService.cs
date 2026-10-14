using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ApisAccessService(
    IConfiguration configuration,
    IOptions<ApisOptions> options,
    IUserNameResolver userNameResolver) : IApisAccessService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private readonly ApisOptions _options = options.Value;

    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = configuration.GetConnectionString(_options.AccessConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.AccessConnectionStringName}' nesukonfiguruota.");
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
}
