namespace DATA_EXCHANGE_HUB.Models;

public sealed record IrisReportSummary(
    long Id,
    string KpiName,
    string Title,
    string SubjectTemplate,
    string SenderKey,
    string DefaultSqlConnectionName,
    bool IsActive,
    int SourceCount,
    int RecipientCount,
    DateTimeOffset CreatedAt);

public sealed record IrisReportSource(
    long Id,
    long ReportId,
    int SourceOrder,
    string SourceType,
    string SqlConnectionName,
    string SqlDatabase,
    string SqlText,
    string SqlFilePath,
    string StoredProcedure,
    string TableTitle,
    bool IncludeHtmlTable,
    bool AttachExcel,
    bool AttachCsv);

public sealed record IrisReportRecipient(
    long Id,
    long ReportId,
    string AddressType,
    string Email,
    string DisplayName,
    string Erp,
    bool IsActive);

public sealed record IrisSchedule(
    long Id,
    long ReportId,
    string ScheduleType,
    string ScheduleJson,
    DateTime? NextRunAt,
    bool IsActive);

public sealed record IrisCreateReportRequest(
    string KpiName,
    string Title,
    string SubjectTemplate,
    string SenderKey,
    string DefaultSqlConnectionName,
    string CreatedBy);

public sealed record IrisUpdateReportRequest(
    long Id,
    string KpiName,
    string Title,
    string SubjectTemplate,
    string SenderKey,
    string DefaultSqlConnectionName);

public sealed record IrisCreateSourceRequest(
    long ReportId,
    string SourceType,
    string SqlConnectionName,
    string SqlDatabase,
    string SqlText,
    string SqlFilePath,
    string StoredProcedure,
    string TableTitle,
    bool IncludeHtmlTable,
    bool AttachExcel,
    bool AttachCsv,
    string CreatedBy);

public sealed record IrisUpdateSourceRequest(
    long Id,
    long ReportId,
    string SourceType,
    string SqlConnectionName,
    string SqlDatabase,
    string SqlText,
    string SqlFilePath,
    string StoredProcedure,
    string TableTitle,
    bool IncludeHtmlTable,
    bool AttachExcel,
    bool AttachCsv);

public sealed record IrisCreateRecipientRequest(
    long ReportId,
    string AddressType,
    string Email,
    string DisplayName,
    string Erp,
    string CreatedBy);

public sealed record IrisCreateScheduleRequest(
    long ReportId,
    string ScheduleType,
    string ScheduleJson,
    DateTime? NextRunAt,
    string CreatedBy);

public sealed record IrisUpdateScheduleRequest(
    long Id,
    long ReportId,
    string ScheduleType,
    string ScheduleJson,
    DateTime? NextRunAt);
