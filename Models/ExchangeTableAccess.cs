namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeTableAccess(
    string TableName,
    string AccessRole,
    ExchangePermission Permission,
    string? BehaviorProfile = null)
{
    public const string CorrectionTypeBehaviorProfile = "CORRECTION_TYPE";

    public bool CanReceive => Permission.HasFlag(ExchangePermission.Receive);

    public bool CanUpload => Permission.HasFlag(ExchangePermission.Upload);

    public bool IsCorrectionTypeProfile =>
        BehaviorProfile?.Equals(CorrectionTypeBehaviorProfile, StringComparison.OrdinalIgnoreCase) == true;

    public bool IsAdmin => AccessRole
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(role => role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase));

    public string PermissionLabel => Permission switch
    {
        ExchangePermission.UploadAndReceive => "Gali gauti ir ikelti",
        ExchangePermission.Receive => "Gali tik gauti",
        ExchangePermission.Upload => "Gali tik ikelti",
        _ => "Prieiga nesuteikta"
    };
}
