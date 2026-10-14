namespace DATA_EXCHANGE_HUB.Services;

public sealed class ClientInfoOptions
{
    public const string SectionName = "ClientInfo";

    public string AppName { get; set; } = "CLIENT_INFO";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string DataConnectionStringName { get; set; } = "STAT3";

    public string QueryPath { get; set; } = "Sql/ClientInfo/STAT3/CLIENT_INFO.sql";

    public int CommandTimeoutSeconds { get; set; } = 180;
}
