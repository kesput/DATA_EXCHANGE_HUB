namespace DATA_EXCHANGE_HUB.Models;

public sealed class OraculOptions
{
    public const string SectionName = "Oracul";

    public string AppName { get; set; } = "ORACUL";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string DataConnectionStringName { get; set; } = "OraculSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string DataProcedureName { get; set; } = "SNTXDATA.dbo.LSP_ORDERS_BY_WEEKDAY";

    public string ReportSourceTableName { get; set; } =
        "BLSTXSTAT1.BLS_LOGIT_STAT.dbo.LSP_ORDERS_BY_WEEKDAY_POS";
}
