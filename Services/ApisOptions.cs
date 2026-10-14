namespace DATA_EXCHANGE_HUB.Services;

public sealed class ApisOptions
{
    public const string SectionName = "Apis";

    public string AppName { get; set; } = "APIS";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public int CommandTimeoutSeconds { get; set; } = 180;

    public string[] QvdRoots { get; set; } = [];
}
