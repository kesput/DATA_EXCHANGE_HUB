namespace DATA_EXCHANGE_HUB.Services;

public interface IWindowsCredentialValidator
{
    bool TryNormalizeUserName(string userName, out string normalizedUserName);

    bool TryValidate(string userName, string password, out string normalizedUserName);
}