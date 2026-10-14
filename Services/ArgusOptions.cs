namespace DATA_EXCHANGE_HUB.Services;

public sealed class ArgusOptions
{
    public const string SectionName = "Argus";

    public string AppName { get; set; } = "ARGUS";

    public string AdminAppName { get; set; } = "ARGUS_ADMIN";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string DataConnectionStringName { get; set; } = "OraculSql";

    public string ServerMetricsTableName { get; set; } = "BLS_LOGIT_STAT.dbo.server_metrics";

    public string ProcessMetricsTableName { get; set; } = "BLS_LOGIT_STAT.dbo.process_metrics";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string StoragePath { get; set; } = @"Q:\QLIKDATA\TASK_STATUS";

    public string IisStoragePath { get; set; } = @"Q:\QLIKDATA\TASK_STATUS";

    public string LocalTaskKillCommandFilePath { get; set; } = @"Q:\TASK_FROM_WEB\WEB_TASK_KILL_PID_LIST.txt";

    public string IisTaskKillCommandFilePath { get; set; } = @"Q:\QLIKDATA\TASK_FROM_WEB\WEB_TASK_KILL_PID_LIST.txt";

    public int TaskStatusReadTimeoutSeconds { get; set; } = 60;

    public int ServerMetricsCommandTimeoutSeconds { get; set; } = 10;
}







