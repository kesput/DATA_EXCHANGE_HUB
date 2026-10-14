namespace DATA_EXCHANGE_HUB.Services;

public sealed class AreteOptions
{
    public const string SectionName = "Arete";

    public string AppName { get; set; } = "ARETE";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public int CommandTimeoutSeconds { get; set; } = 180;
}
