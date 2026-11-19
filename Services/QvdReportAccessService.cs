namespace DATA_EXCHANGE_HUB.Services;

public interface IQvdReportAccessService
{
    Task<bool> CanReadAsync(string module, CancellationToken cancellationToken = default);
}

public sealed class QvdReportAccessService(
    IHttpContextAccessor httpContextAccessor,
    IUserNameResolver userNameResolver,
    IQueryCountryAccessService countryAccess,
    ILogger<QvdReportAccessService> logger) : IQvdReportAccessService
{
    public const string AdminOnlyMessage = "QvdReport.AdminOnly";
    private readonly Dictionary<string, Task<bool>> checks = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> CanReadAsync(string module, CancellationToken cancellationToken = default)
    {
        var app = module.ToUpperInvariant();
        var context = httpContextAccessor.HttpContext;
        if (app is not ("APIS" or "ARETE" or "HYDRA" or "KAIROS") || context is null)
            return Task.FromResult(false);
        var userName = userNameResolver.ResolveUserName(context.User);
        if (string.IsNullOrWhiteSpace(userName)) return Task.FromResult(false);
        var key = userName + ":" + app;
        if (!checks.TryGetValue(key, out var check))
            checks[key] = check = CheckAsync(context, app, cancellationToken);
        return check;
    }

    private async Task<bool> CheckAsync(HttpContext context, string app, CancellationToken ct)
    {
        try { return await countryAccess.HasAdminAccessAsync(context.User, app, "DataExchangeSql", ct); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "QVD ADMIN access check failed for {Module}; access denied.", app);
            return false;
        }
    }

    public static bool IsQvdQuery(string queryId, string module)
    {
        var parts = queryId.Replace('\\', '/').Trim('/').Split('/');
        return parts.Length >= 5 && parts[0].Equals("Sql", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals(module, StringComparison.OrdinalIgnoreCase) && parts[3].Equals("QVD", StringComparison.OrdinalIgnoreCase);
    }
}
