namespace DATA_EXCHANGE_HUB.Models;

public sealed class KairosOptions
{
    public const string SectionName = "Kairos";

    public string AppName { get; set; } = "KAIROS";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string DataConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string RequestTableName { get; set; } = "BLS_LOGIT_STAT.dbo.KAIROS_REQUESTS";

    public string LogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.KAIROS_REQUEST_LOG";

    public string PromoForecastTableName { get; set; } = "BLS_LOGIT_STAT.dbo.KAIROS_PROMO_FORECAST";

    public int ListLimit { get; set; } = 100;

    public int LogLimit { get; set; } = 30;
}


