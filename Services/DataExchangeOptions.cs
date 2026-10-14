namespace DATA_EXCHANGE_HUB.Services;

public sealed class DataExchangeOptions
{
    public const string SectionName = "DataExchange";

    public bool TestMode { get; set; } = true;

    public string StoragePath { get; set; } = "App_Data/DataExchange";

    public string ConnectionStringName { get; set; } = "DataExchangeSql";

    public string UploadTableName { get; set; } = "dbo.DataExchangeFiles";

    public string ExchangeAccessTableName { get; set; } = "BLS_LOGIT_STAT.dbo.BS_EXCHANGE_ACCESS";

    public string ExchangeLogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.BS_EXCHANGE_LOG";

    public string TestModeTableName { get; set; } = "dbo.DataExchangeFiles";

    public int MaxFileSizeMb { get; set; } = 50;

    public long MaxFileSizeBytes => Math.Max(1, MaxFileSizeMb) * 1024L * 1024L;
}
