using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.Extensions.Options;
using System.Security.Principal;

namespace DATA_EXCHANGE_HUB.Pages.Hub;

public sealed class LoginModel(
    IOptions<WorkHubOptions> options,
    IWebHostEnvironment environment,
    IWindowsCredentialValidator credentialValidator,
    IHubSecurityAlertService securityAlertService,
    ILogger<LoginModel> logger,
    IAuthenticationSchemeProvider authenticationSchemes) : PageModel
{
    public const string SessionKey = "WorkHubAuthenticated";

    public const string PasswordSessionKey = "WorkHubPasswordAuthenticated";

    public const string BlockedSessionKey = "WorkHubLoginBlocked";

    private readonly WorkHubOptions _options = options.Value;

    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/Hub/Index";

    public string? ErrorMessage { get; private set; }
    public bool WindowsAuthenticationRequired { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(ReturnUrl))
        {
            ReturnUrl = "/Hub/Index";
        }

        var activeWindowsUserName = ResolveServerWindowsUserName();
        if (string.IsNullOrWhiteSpace(activeWindowsUserName) && !environment.IsDevelopment())
        {
            WindowsAuthenticationRequired = true;
            ClearHubLoginSession();
            if (await authenticationSchemes.GetSchemeAsync(IISDefaults.AuthenticationScheme) is not null)
                return Challenge(IISDefaults.AuthenticationScheme);
            // Plain Kestrel cannot perform IIS authentication. Show a meaningful
            // 401 instead of offering a password form that cannot succeed.
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            ErrorMessage = "Hub.Security.WindowsRequired";
            return Page();
        }
        if (HttpContext.Session.GetString(BlockedSessionKey) == "1")
        {
            ErrorMessage = "Hub.Security.LoginHelp";
            return Page();
        }
        if (!string.IsNullOrWhiteSpace(activeWindowsUserName))
        {
            HttpContext.Session.SetString(SessionKey, "1");
            HttpContext.Session.SetString(PasswordSessionKey, "0");
            HttpContext.Session.Remove(WindowsUserNameResolver.AuthenticatedUserSessionKey);
            return LocalRedirect(GetSafeReturnUrl(ReturnUrl));
        }

        ClearHubLoginSession();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // A rejected login must never leave a previous authenticated session usable.
        ClearHubLoginSession();
        HttpContext.Session.SetString(BlockedSessionKey, "1");
        var activeUserName = ResolveActiveWindowsUserName();
        if (string.IsNullOrWhiteSpace(activeUserName))
        {
            WindowsAuthenticationRequired = true;
            ErrorMessage = "Hub.Security.WindowsRequired";
            return Page();
        }
        var hasAttemptedUserName = credentialValidator.TryNormalizeUserName(UserName, out var attemptedUserName);
        if (!hasAttemptedUserName)
        {
            ErrorMessage = "Neteisingas vartotojo vardas arba slaptazodis.";
            return Page();
        }
        if (!string.IsNullOrWhiteSpace(activeUserName) &&
            hasAttemptedUserName &&
            !IsSameUser(activeUserName, attemptedUserName))
        {
            if (credentialValidator.TryValidate(UserName, Password, out var validatedAttemptedUserName))
            {
                await TryNotifySuspiciousLoginAsync(activeUserName, validatedAttemptedUserName);
            }

            ErrorMessage = "Hub.Security.OtherUser";
            return Page();
        }

        if (credentialValidator.TryValidate(UserName, Password, out var authenticatedUserName))
        {
            HttpContext.Session.Remove(BlockedSessionKey);
            HttpContext.Session.SetString(SessionKey, "1");
            HttpContext.Session.SetString(PasswordSessionKey, "0");
            HttpContext.Session.SetString(
                WindowsUserNameResolver.AuthenticatedUserSessionKey,
                authenticatedUserName);

            return LocalRedirect(GetSafeReturnUrl(ReturnUrl));
        }

        if (environment.IsDevelopment() &&
            string.Equals(Password, _options.Password, StringComparison.Ordinal))
        {
            HttpContext.Session.Remove(BlockedSessionKey);
            HttpContext.Session.SetString(SessionKey, "1");
            HttpContext.Session.SetString(PasswordSessionKey, "1");
            HttpContext.Session.Remove(WindowsUserNameResolver.AuthenticatedUserSessionKey);
            return LocalRedirect(GetSafeReturnUrl(ReturnUrl));
        }

        ErrorMessage = "Neteisingas vartotojo vardas arba slaptazodis.";
        return Page();
    }

    private void ClearHubLoginSession()
    {
        HttpContext.Session.Remove(SessionKey);
        HttpContext.Session.Remove(PasswordSessionKey);
        HttpContext.Session.Remove(WindowsUserNameResolver.AuthenticatedUserSessionKey);
    }

    private async Task TryNotifySuspiciousLoginAsync(string activeUserName, string attemptedUserName)
    {
        try
        {
            await securityAlertService.NotifySuspiciousLoginAsync(
                activeUserName,
                attemptedUserName,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                DateTimeOffset.Now,
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Nepavyko issiusti saugumo pranesimo. Active user {ActiveUser} attempted {AttemptedUser}.",
                activeUserName,
                attemptedUserName);
        }
    }

    private string ResolveActiveWindowsUserName()
    {
        var serverUserName = ResolveServerWindowsUserName();
        if (!string.IsNullOrWhiteSpace(serverUserName))
        {
            return serverUserName;
        }

        return environment.IsDevelopment()
            ? ResolveLocalWindowsUserName()
            : string.Empty;
    }

    private string ResolveServerWindowsUserName()
    {
        foreach (var serverVariable in new[] { "LOGON_USER", "REMOTE_USER", "AUTH_USER" })
        {
            var value = HttpContext.Features.Get<IServerVariablesFeature>()?[serverVariable];
            if (IsRealWindowsUserName(value))
            {
                return value!.Trim();
            }
        }

        var identityName = User.Identity?.Name;
        if (User.Identity?.IsAuthenticated == true && IsRealWindowsUserName(identityName))
        {
            return identityName!.Trim();
        }

        return string.Empty;
    }

    private static string ResolveLocalWindowsUserName()
    {
        var environmentUserName = $@"{Environment.UserDomainName}\{Environment.UserName}";
        if (IsRealWindowsUserName(environmentUserName))
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

    private static bool IsSameUser(string leftUserName, string rightUserName)
    {
        var left = NormalizeUserName(leftUserName);
        var right = NormalizeUserName(rightUserName);
        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUserName(string userName)
    {
        return string.IsNullOrWhiteSpace(userName) ? string.Empty : userName.Trim();
    }

    private static bool IsRealWindowsUserName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var normalized = userName.Trim();
        return !normalized.StartsWith(@"IIS APPPOOL\", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("ApplicationPoolIdentity", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeReturnUrl(string returnUrl)
    {
        return string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal) ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? "/Hub/Index"
            : returnUrl;
    }
}
