namespace DATA_EXCHANGE_HUB.Services;

public sealed class HydraOptions
{
    public const string SectionName = "Hydra";

    public string AppName { get; set; } = "HYDRA";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public int CommandTimeoutSeconds { get; set; } = 180;
}
