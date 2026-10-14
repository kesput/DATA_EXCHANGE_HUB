namespace DATA_EXCHANGE_HUB.Models;

public sealed class AtlasOptions
{
    public const string SectionName = "Atlas";

    public string AppName { get; set; } = "ATLAS";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string DataConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string AccessTableName { get; set; } = "BLS_LOGIT_STAT.dbo.BS_APP_ACCESS";

    public string PlanTableName { get; set; } = "BLS_LOGIT_STAT.dbo.ATLAS_PLANS";

    public string UserSettingsTableName { get; set; } = "BLS_LOGIT_STAT.dbo.ATLAS_USER_SETTINGS";
}
