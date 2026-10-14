namespace DATA_EXCHANGE_HUB.Services;

public sealed class IrisOptions
{
    public const string SectionName = "Iris";

    public string ConnectionStringName { get; set; } = "DataExchangeSql";

    public string ReportTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_REPORTS";

    public string SourceTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_REPORT_SOURCES";

    public string RecipientTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_REPORT_RECIPIENTS";

    public string ScheduleTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_SCHEDULES";

    public string RunTableName { get; set; } = "BLS_LOGIT_STAT.dbo.IRIS_RUNS";
}
