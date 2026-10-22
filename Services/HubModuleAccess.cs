using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;

namespace DATA_EXCHANGE_HUB.Services;

public interface IHubModuleAccess
{
    Task<bool> CanOpenAsync(string path, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

// The catalogue and direct requests use the same policy. Existing services retain
// their module-specific country, table and administrator rules.
public sealed class HubModuleAccess(
    IServiceProvider services,
    IUserNameResolver userNameResolver,
    IConfiguration configuration,
    ILogger<HubModuleAccess> logger) : IHubModuleAccess
{
    private readonly Dictionary<string, Task<bool>> checks = new(StringComparer.OrdinalIgnoreCase);

    public static string Module(string path) => path.Split('?', 2)[0].Trim('/').Split('/')[0].ToUpperInvariant();

    public static string? ApplicationName(string path) => Module(path) switch
    {
        "DATAEXCHANGE" => "Data exchange", "FILEUPLOAD" => "File upload",
        "SENDMAILFILE" => "Send Mail File", "CLIENTINFO" => "Klientu info",
        "SENDMAIL" => "SEND MAIL",
        "ORACUL" or "KAIROS" or "ATLAS" or "HYDRA" or "DIKE" or "ARETE" or
        "APIS" or "ARGUS" or "THEMIS" or "SPHINX" or "SESHAT" or "IRIS" => Module(path),
        _ => null
    };

    public Task<bool> CanOpenAsync(string path, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var module = Module(path);
        var key = userNameResolver.ResolveUserName(user) + ":" + module;
        if (!checks.TryGetValue(key, out var check))
            checks[key] = check = CheckAsync(module, user, cancellationToken);
        return check;
    }

    private async Task<bool> CheckAsync(string module, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userNameResolver.ResolveUserName(user))) return false;
        try
        {
            return module switch
            {
                "DATAEXCHANGE" => (await services.GetRequiredService<IExchangeAccessService>().GetCurrentUserAccessAsync(user, ct)).CanOpen,
                "ORACUL" => await services.GetRequiredService<IOraculService>().HasAccessAsync(user, ct),
                "KAIROS" => await services.GetRequiredService<IKairosService>().HasAccessAsync(user, ct),
                "ATLAS" => await services.GetRequiredService<IAtlasService>().HasAccessAsync(user, ct),
                "HYDRA" => await services.GetRequiredService<IHydraAccessService>().HasAccessAsync(user, ct),
                "DIKE" => await services.GetRequiredService<IDikeAccessService>().HasAccessAsync(user, ct),
                "ARETE" => await services.GetRequiredService<IAreteAccessService>().HasAccessAsync(user, ct),
                "APIS" => await services.GetRequiredService<IApisAccessService>().HasAccessAsync(user, ct),
                "ARGUS" => (await services.GetRequiredService<IArgusAccessService>().GetAccessAsync(user, ct)).CanOpen,
                "THEMIS" => await services.GetRequiredService<IThemisService>().HasAccessAsync(user, ct),
                "FILEUPLOAD" => await services.GetRequiredService<IFileUploadAccessService>().HasAccessAsync(user, ct),
                "SENDMAILFILE" => await services.GetRequiredService<ISendMailFileService>().HasAnyAccessAsync(user, ct),
                "CLIENTINFO" => await services.GetRequiredService<IClientInfoService>().HasAccessAsync(user, ct),
                "SPHINX" => (await services.GetRequiredService<ISphinxService>().GetAccessAsync(user, ct)).CanOpen,
                "SESHAT" => (await services.GetRequiredService<ISeshatService>().GetAccessAsync(user, ct)).CanOpen,
                "IRIS" or "SENDMAIL" => await CheckApplicationAsync(module == "SENDMAIL" ? "SEND_MAIL" : module, user, ct),
                _ => false
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "HUB module access check failed for {Module}; access denied.", module);
            return false;
        }
    }

    private async Task<bool> CheckApplicationAsync(string app, ClaimsPrincipal user, CancellationToken ct)
    {
        var section = app == "SEND_MAIL" ? "SendMail" : "Iris";
        var connectionName = configuration[$"{section}:AccessConnectionStringName"] ?? "DataExchangeSql";
        var procedure = SqlName.NormalizeTableName(configuration[$"{section}:AccessProcedureName"] ??
            "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess", "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess");
        await using var connection = new SqlConnection(configuration.GetConnectionString(connectionName));
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"EXEC {procedure} @AppName, @UserName", connection);
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = configuration[$"{section}:AppName"] ?? app;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userNameResolver.ResolveUserName(user);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct);
    }
}
