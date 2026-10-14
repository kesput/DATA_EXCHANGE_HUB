namespace DATA_EXCHANGE_HUB.Services;

public sealed class WorkHubOptions
{
    public const string SectionName = "WorkHub";

    public string Password { get; set; } = "hub";

    public string DefaultDomain { get; set; } = string.Empty;

    public string AccessRequestConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessRequestTableName { get; set; } = "BLS_LOGIT_STAT.dbo.HUB_ACCESS_REQUESTS";

    public string TestAccessRequestPhone { get; set; } = string.Empty;

    public string FeedbackConnectionStringName { get; set; } = "DataExchangeSql";

    public string FeedbackSuggestionTableName { get; set; } = "BLS_LOGIT_STAT.dbo.HUB_SUGGESTIONS";
}