using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class WindowsCredentialValidator(IOptions<WorkHubOptions> options) : IWindowsCredentialValidator
{
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;

    private readonly WorkHubOptions _options = options.Value;

    public bool TryNormalizeUserName(string userName, out string normalizedUserName)
    {
        normalizedUserName = string.Empty;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var normalizedInput = userName.Trim();
        if (normalizedInput.Contains('\\', StringComparison.Ordinal) ||
            normalizedInput.Contains('/', StringComparison.Ordinal) ||
            normalizedInput.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        var parsed = ParseUserName(normalizedInput);
        if (string.IsNullOrWhiteSpace(parsed.UserName))
        {
            return false;
        }

        normalizedUserName = parsed.NormalizedUserName;
        return true;
    }

    public bool TryValidate(string userName, string password, out string normalizedUserName)
    {
        normalizedUserName = string.Empty;

        if (string.IsNullOrEmpty(password) ||
            !TryNormalizeUserName(userName, out normalizedUserName))
        {
            return false;
        }

        var parsed = ParseUserName(userName.Trim());
        var token = IntPtr.Zero;
        try
        {
            var isValid = LogonUser(
                parsed.UserName,
                parsed.Domain,
                password,
                Logon32LogonNetwork,
                Logon32ProviderDefault,
                out token);

            return isValid;
        }
        catch (Win32Exception)
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    private ParsedUserName ParseUserName(string userName)
    {
        var defaultDomain = _options.DefaultDomain?.Trim();
        if (string.IsNullOrWhiteSpace(defaultDomain))
        {
            defaultDomain = Environment.UserDomainName;
        }

        var normalized = string.IsNullOrWhiteSpace(defaultDomain)
            ? userName
            : $@"{defaultDomain}\{userName}";

        return new ParsedUserName(
            string.IsNullOrWhiteSpace(defaultDomain) ? null : defaultDomain,
            userName,
            normalized);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername,
        string? lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private sealed record ParsedUserName(string? Domain, string UserName, string NormalizedUserName);
}