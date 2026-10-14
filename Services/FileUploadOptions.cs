namespace DATA_EXCHANGE_HUB.Services;

public sealed class FileUploadOptions
{
    public const string SectionName = "FileUpload";

    public string AppName { get; set; } = "FILE_UPLOAD";

    public string AccessConnectionStringName { get; set; } = "DataExchangeSql";

    public string AccessProcedureName { get; set; } = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";

    public string LogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.FILE_UPLOAD_LOG";

    public string NotificationTableName { get; set; } = "BLS_LOGIT_STAT.dbo.FILE_UPLOAD_NOTIFICATIONS";

    public string StoragePath { get; set; } = @"Q:\QLIKDATA\EXCHANGE_UPLOAD";

    public int MaxFileSizeMb { get; set; } = 100;

    public long MaxFileSizeBytes => Math.Max(1, MaxFileSizeMb) * 1024L * 1024L;

    public int MaxStorageSizeGb { get; set; } = 2;

    public long MaxStorageSizeBytes => Math.Max(1, MaxStorageSizeGb) * 1024L * 1024L * 1024L;
}


