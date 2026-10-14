using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Http.Features;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class WindowsUserNameResolver(
    IHttpContextAccessor httpContextAccessor,
    IWebHostEnvironment environment) : IUserNameResolver
{
    private const string SessionUserNameKey = "DATA_EXCHANGE_HUB_WINDOWS_USER";

    public const string AuthenticatedUserSessionKey = "DATA_EXCHANGE_HUB_AUTHENTICATED_USER";

    public string ResolveUserName(ClaimsPrincipal user)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var serverUserName = ResolveServerUserName(httpContext);
        if (IsRealUserName(serverUserName))
        {
            return RememberUserName(httpContext, serverUserName);
        }

        var identityName = user.Identity?.Name;
        if (user.Identity?.IsAuthenticated == true &&
            IsRealUserName(identityName))
        {
            return RememberUserName(httpContext, identityName!);
        }

        var authenticatedUserName = ReadAuthenticatedUserName(httpContext);
        if (IsRealUserName(authenticatedUserName))
        {
            return authenticatedUserName.Trim();
        }

        if (environment.IsDevelopment())
        {
            var localUserName = ResolveLocalUserName();
            if (IsRealUserName(localUserName))
            {
                return localUserName;
            }
        }

        return string.Empty;
    }

    private static string RememberUserName(HttpContext? httpContext, string userName)
    {
        var normalized = userName.Trim();

        try
        {
            httpContext?.Session.SetString(SessionUserNameKey, normalized);
        }
        catch (InvalidOperationException)
        {
            // Session may be unavailable before middleware is initialized.
        }

        return normalized;
    }

    private static string ReadAuthenticatedUserName(HttpContext? httpContext)
    {
        try
        {
            return httpContext?.Session.GetString(AuthenticatedUserSessionKey) ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string ResolveServerUserName(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return string.Empty;
        }

        var feature = httpContext.Features.Get<IServerVariablesFeature>();
        foreach (var key in new[] { "LOGON_USER", "REMOTE_USER", "AUTH_USER" })
        {
            var value = feature?[key];
            if (IsRealUserName(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    private static string ResolveLocalUserName()
    {
        var environmentUserName = $@"{Environment.UserDomainName}\{Environment.UserName}";
        if (IsRealUserName(environmentUserName))
        {
            return environmentUserName;
        }

        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            return WindowsIdentity.GetCurrent()?.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsRealUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var normalized = userName.Trim();
        return !normalized.StartsWith(@"IIS APPPOOL\", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("ApplicationPoolIdentity", StringComparison.OrdinalIgnoreCase);
    }
}