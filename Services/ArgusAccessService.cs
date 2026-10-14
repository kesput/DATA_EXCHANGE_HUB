using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ArgusAccessService(
    IConfiguration configuration,
    IOptions<ArgusOptions> options,
    IUserNameResolver userNameResolver) : IArgusAccessService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private readonly ArgusOptions _options = options.Value;

    public async Task<ArgusAccessState> GetAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var isUser = await HasAccessAsync(_options.AppName, userName, cancellationToken);
        var isAdmin = await HasAccessAsync(_options.AdminAppName, userName, cancellationToken);

        return new ArgusAccessState(isUser || isAdmin, isAdmin);
    }

    private async Task<bool> HasAccessAsync(
        string appName,
        string userName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            DefaultAccessProcedureName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = appName;
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

