namespace DATA_EXCHANGE_HUB.Services;

public sealed class DikeOptions
{
    public const string SectionName = "Dike";

    public string AppName { get; set; } = "DIKE";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string ConfigTableName { get; set; } = "BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_CONFIG";

    public string LogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_LOG";

    public string Stat3ConnectionStringName { get; set; } = "STAT3";
}
