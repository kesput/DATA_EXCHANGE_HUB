namespace DATA_EXCHANGE_HUB.Services;

public sealed class SendMailFileOptions
{
    public const string SectionName = "SendMailFile";

    public string AppName { get; set; } = "SEND_MAIL_FILE";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    public Dictionary<string, string> CountryAppNames { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LV"] = "SEND_MAIL_FILE_LV",
        ["LT"] = "SEND_MAIL_FILE_LT",
        ["EE"] = "SEND_MAIL_FILE_EE"
    };
    public Dictionary<string, string> LocalCountryPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LV"] = @"Q:\DATA\SEND_MAIL\LV",
        ["LT"] = @"Q:\DATA\SEND_MAIL\LT",
        ["EE"] = @"Q:\DATA\SEND_MAIL\EE"
    };

    public Dictionary<string, string> IisCountryPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LV"] = @"Q:\QLIKDATA\DATA\SEND_MAIL\LV",
        ["LT"] = @"Q:\QLIKDATA\DATA\SEND_MAIL\LT",
        ["EE"] = @"Q:\QLIKDATA\DATA\SEND_MAIL\EE"
    };
}
