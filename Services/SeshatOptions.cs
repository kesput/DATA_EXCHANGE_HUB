namespace DATA_EXCHANGE_HUB.Services;

public sealed class SeshatOptions
{
    public const string SectionName = "Seshat";

    public string ConnectionStringName { get; set; } = "DataExchangeSql";

    public string AppName { get; set; } = "SESHAT";

    public string DevelopmentPasswordUserName { get; set; } = "ADMIN";

    public string StoragePath { get; set; } = "App_Data/DataExchange/Seshat";

    public string DocumentsTableName { get; set; } = "BLS_LOGIT_STAT.dbo.SESHAT_DOCUMENTS";

    public string DocumentCountriesTableName { get; set; } = "BLS_LOGIT_STAT.dbo.SESHAT_DOCUMENT_COUNTRIES";

    public string FilesTableName { get; set; } = "BLS_LOGIT_STAT.dbo.SESHAT_FILES";

    public string SectionsTableName { get; set; } = "BLS_LOGIT_STAT.dbo.SESHAT_SECTIONS";

    public string LogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.SESHAT_LOG";

    public string AccessTableName { get; set; } = "BLS_LOGIT_STAT.dbo.BS_APP_ACCESS";

    public string AccessAppNameColumn { get; set; } = "APP_NAME";

    public string AccessUserColumn { get; set; } = "DOMEN_USER";

    public string AccessLevelColumn { get; set; } = "ACCESS";

    public string AccessDimensionColumn { get; set; } = "DIMENSION";

    public string AccessDimensionValueColumn { get; set; } = "COUNTRY";

    public string CountryDimensionName { get; set; } = "COUNTRY";

    public bool UseAccessDimensionColumn { get; set; } = false;

    public string AccessActiveColumn { get; set; } = "ACTIVE";

    public bool UseAccessActiveColumn { get; set; } = true;

    public int MaxFileSizeMb { get; set; } = 100;

    public long MaxFileSizeBytes => Math.Max(1, MaxFileSizeMb) * 1024L * 1024L;

    public int ListLimit { get; set; } = 100;

    public int LogLimit { get; set; } = 50;
}






