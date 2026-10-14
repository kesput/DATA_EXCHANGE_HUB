namespace DATA_EXCHANGE_HUB.Services;

public sealed class SphinxOptions
{
    public const string SectionName = "Sphinx";

    public string AppName { get; set; } = "SPHINX";
    public string AdminAppName { get; set; } = "SPHINX_ADMIN";
    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";
    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    public string ConnectionStringName { get; init; } = "DataExchangeSql";
    public string SurveyTableName { get; init; } = "BLS_LOGIT_STAT.dbo.SPHINX_SURVEYS";
    public string QuestionTableName { get; init; } = "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_QUESTIONS";
    public string OptionTableName { get; init; } = "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_OPTIONS";
    public string ResponseTableName { get; init; } = "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_RESPONSES";
    public string AnswerTableName { get; init; } = "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_ANSWERS";
}
