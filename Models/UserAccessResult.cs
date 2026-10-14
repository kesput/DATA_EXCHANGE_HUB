namespace DATA_EXCHANGE_HUB.Models;

public sealed record UserAccessResult(
    string UserName,
    ExchangePermission Permission,
    bool IsTestMode)
{
    public bool CanOpen => Permission != ExchangePermission.None;

    public bool CanReceive => Permission.HasFlag(ExchangePermission.Receive);

    public bool CanUpload => Permission.HasFlag(ExchangePermission.Upload);

    public string PermissionLabel => Permission switch
    {
        ExchangePermission.UploadAndReceive => "Gali gauti ir ikelti",
        ExchangePermission.Receive => "Gali tik gauti",
        ExchangePermission.Upload => "Gali tik ikelti",
        _ => "Prieiga nesuteikta"
    };
}
