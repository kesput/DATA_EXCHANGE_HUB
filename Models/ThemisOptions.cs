namespace DATA_EXCHANGE_HUB.Models;

public sealed class ThemisOptions
{
    public const string SectionName = "Themis";

    public string AppName { get; set; } = "THEMIS";

    public string ConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string LogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.SL_CONFIG_LOG";

    public List<string> TableNames { get; set; } = [];
}
