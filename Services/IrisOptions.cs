namespace DATA_EXCHANGE_HUB.Services;

public sealed class IrisOptions
{
    public const string SectionName = "Iris";

    public int PreviewTimeoutSeconds { get; set; } = 30;
    public int PreviewMaxRows { get; set; } = 500;
    public string PreviewLogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_QUERY_PREVIEW_LOG";
    public string[] PreviewSqlRoots { get; set; } = [];

    public string ConnectionStringName { get; set; } = "DataExchangeSql";

    public string ReportTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_REPORTS";

    public string SourceTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_REPORT_SOURCES";

    public string RecipientTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_REPORT_RECIPIENTS";

    public string ScheduleTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_SCHEDULES";

    public string RunTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_RUNS";
}
