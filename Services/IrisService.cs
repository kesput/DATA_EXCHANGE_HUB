using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class IrisService(
    IConfiguration configuration,
    IOptions<IrisOptions> options) : IIrisService
{
    private readonly IrisOptions _options = options.Value;

    private string ReportTable => SqlName.NormalizeTableName(_options.ReportTableName, "BLS_LOGIT_STAT.dbo.IRIS_REPORTS");
    private string SourceTable => SqlName.NormalizeTableName(_options.SourceTableName, "BLS_LOGIT_STAT.dbo.IRIS_REPORT_SOURCES");
    private string RecipientTable => SqlName.NormalizeTableName(_options.RecipientTableName, "BLS_LOGIT_STAT.dbo.IRIS_REPORT_RECIPIENTS");
    private string ScheduleTable => SqlName.NormalizeTableName(_options.ScheduleTableName, "BLS_LOGIT_STAT.dbo.IRIS_SCHEDULES");
    private string RunTable => SqlName.NormalizeTableName(_options.RunTableName, "BLS_LOGIT_STAT.dbo.IRIS_RUNS");

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        foreach (var script in BuildSchemaScripts())
        {
            await using var command = new SqlCommand(script, connection);
            command.CommandTimeout = 60;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<IrisReportSummary>> ListReportsAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var reports = new List<IrisReportSummary>();
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var hasSubjectTemplate = await HasColumnAsync(connection, ReportTable, "SubjectTemplate", cancellationToken);
        var subjectTemplateSelect = hasSubjectTemplate
            ? "r.SubjectTemplate AS SubjectTemplate"
            : "r.Title AS SubjectTemplate";
        var subjectTemplateGroup = hasSubjectTemplate ? ", r.SubjectTemplate" : string.Empty;
        await using var command = new SqlCommand($"""
            SELECT r.Id, r.KpiName, r.Title, {subjectTemplateSelect},
                   r.SenderKey, r.DefaultSqlConnectionName, r.IsActive,
                   COUNT(DISTINCT s.Id) AS SourceCount,
                   COUNT(DISTINCT rec.Id) AS RecipientCount,
                   r.CreatedAt
            FROM {ReportTable} r
            LEFT JOIN {SourceTable} s ON s.ReportId = r.Id
            LEFT JOIN {RecipientTable} rec ON rec.ReportId = r.Id AND rec.IsActive = 1
            GROUP BY r.Id, r.KpiName, r.Title{subjectTemplateGroup}, r.SenderKey, r.DefaultSqlConnectionName, r.IsActive, r.CreatedAt
            ORDER BY r.CreatedAt DESC, r.Id DESC;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            reports.Add(new IrisReportSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero)));
        }

        return reports;
    }

    public async Task<IReadOnlyList<IrisReportSource>> ListSourcesAsync(long reportId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var sources = new List<IrisReportSource>();
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SELECT Id, ReportId, SourceOrder, SourceType, SqlConnectionName, SqlDatabase,
                   SqlText, SqlFilePath, StoredProcedure, TableTitle, IncludeHtmlTable, AttachExcel, AttachCsv
            FROM {SourceTable}
            WHERE ReportId = @ReportId
            ORDER BY SourceOrder, Id;
            """, connection);
        command.Parameters.AddWithValue("@ReportId", reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sources.Add(new IrisReportSource(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12)));
        }

        return sources;
    }

    public async Task<IReadOnlyList<IrisReportRecipient>> ListRecipientsAsync(long reportId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var recipients = new List<IrisReportRecipient>();
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SELECT Id, ReportId, AddressType, Email, DisplayName, Erp, IsActive
            FROM {RecipientTable}
            WHERE ReportId = @ReportId
            ORDER BY IsActive DESC, AddressType, Email;
            """, connection);
        command.Parameters.AddWithValue("@ReportId", reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(new IrisReportRecipient(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return recipients;
    }

    public async Task<IReadOnlyList<IrisSchedule>> ListSchedulesAsync(long reportId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var schedules = new List<IrisSchedule>();
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SELECT Id, ReportId, ScheduleType, ScheduleJson, NextRunAt, IsActive
            FROM {ScheduleTable}
            WHERE ReportId = @ReportId
            ORDER BY IsActive DESC, CreatedAt DESC, Id DESC;
            """, connection);
        command.Parameters.AddWithValue("@ReportId", reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schedules.Add(new IrisSchedule(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.GetBoolean(5)));
        }

        return schedules;
    }

    public async Task<long> CreateReportAsync(IrisCreateReportRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var hasSubjectTemplate = await HasColumnAsync(connection, ReportTable, "SubjectTemplate", cancellationToken);
        var columns = hasSubjectTemplate
            ? "KpiName, Title, SubjectTemplate, SenderKey, DefaultSqlConnectionName, CreatedBy"
            : "KpiName, Title, SenderKey, DefaultSqlConnectionName, CreatedBy";
        var values = hasSubjectTemplate
            ? "@KpiName, @Title, @SubjectTemplate, @SenderKey, @DefaultSqlConnectionName, @CreatedBy"
            : "@KpiName, @Title, @SenderKey, @DefaultSqlConnectionName, @CreatedBy";
        await using var command = new SqlCommand($"""
            INSERT INTO {ReportTable}
                ({columns})
            OUTPUT INSERTED.Id
            VALUES
                ({values});
            """, connection);
        Add(command, "@KpiName", request.KpiName, 260);
        Add(command, "@Title", request.Title, 500);
        if (hasSubjectTemplate)
        {
            Add(command, "@SubjectTemplate", string.IsNullOrWhiteSpace(request.SubjectTemplate) ? request.Title : request.SubjectTemplate, 500);
        }

        Add(command, "@SenderKey", request.SenderKey, 80);
        Add(command, "@DefaultSqlConnectionName", request.DefaultSqlConnectionName, 128);
        Add(command, "@CreatedBy", request.CreatedBy, 260);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateReportAsync(IrisUpdateReportRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Pasirinkite IRIS taska redagavimui.");
        }

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var hasSubjectTemplate = await HasColumnAsync(connection, ReportTable, "SubjectTemplate", cancellationToken);
        var subjectTemplateSet = hasSubjectTemplate
            ? "SubjectTemplate = @SubjectTemplate,"
            : string.Empty;
        await using var command = new SqlCommand($"""
            UPDATE {ReportTable}
            SET KpiName = @KpiName,
                Title = @Title,
                {subjectTemplateSet}
                SenderKey = @SenderKey,
                DefaultSqlConnectionName = @DefaultSqlConnectionName,
                UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", request.Id);
        Add(command, "@KpiName", request.KpiName, 260);
        Add(command, "@Title", request.Title, 500);
        if (hasSubjectTemplate)
        {
            Add(command, "@SubjectTemplate", string.IsNullOrWhiteSpace(request.SubjectTemplate) ? request.Title : request.SubjectTemplate, 500);
        }

        Add(command, "@SenderKey", request.SenderKey, 80);
        Add(command, "@DefaultSqlConnectionName", request.DefaultSqlConnectionName, 128);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("IRIS taskas nerastas.");
        }
    }

    public async Task<long> CreateSourceAsync(IrisCreateSourceRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var sourceType = NormalizeSourceType(request.SourceType);
        ValidateSource(sourceType, request.ReportId, request.SqlConnectionName, request.SqlText, request.SqlFilePath, request.StoredProcedure);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var nextOrder = await GetNextSourceOrderAsync(connection, request.ReportId, cancellationToken);

        await using var command = new SqlCommand($"""
            INSERT INTO {SourceTable}
                (ReportId, SourceOrder, SourceType, SqlConnectionName, SqlDatabase, SqlText,
                 SqlFilePath, StoredProcedure, TableTitle, IncludeHtmlTable, AttachExcel, AttachCsv, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES
                (@ReportId, @SourceOrder, @SourceType, @SqlConnectionName, @SqlDatabase, @SqlText,
                 @SqlFilePath, @StoredProcedure, @TableTitle, @IncludeHtmlTable, @AttachExcel, @AttachCsv, @CreatedBy);
            """, connection);
        command.Parameters.AddWithValue("@ReportId", request.ReportId);
        command.Parameters.AddWithValue("@SourceOrder", nextOrder);
        Add(command, "@SourceType", sourceType, 40);
        Add(command, "@SqlConnectionName", request.SqlConnectionName, 128);
        Add(command, "@SqlDatabase", request.SqlDatabase, 128);
        Add(command, "@SqlText", sourceType == "INLINE_SQL" ? request.SqlText : string.Empty, -1);
        Add(command, "@SqlFilePath", sourceType == "SQL_FILE" ? request.SqlFilePath : string.Empty, 2048);
        Add(command, "@StoredProcedure", sourceType == "STORED_PROCEDURE" ? request.StoredProcedure : string.Empty, 512);
        Add(command, "@TableTitle", request.TableTitle, 260);
        command.Parameters.AddWithValue("@IncludeHtmlTable", request.IncludeHtmlTable);
        command.Parameters.AddWithValue("@AttachExcel", request.AttachExcel);
        command.Parameters.AddWithValue("@AttachCsv", request.AttachCsv);
        Add(command, "@CreatedBy", request.CreatedBy, 260);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateSourceAsync(IrisUpdateSourceRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Pasirinkite SQL saltini redagavimui.");
        }

        var sourceType = NormalizeSourceType(request.SourceType);
        ValidateSource(sourceType, request.ReportId, request.SqlConnectionName, request.SqlText, request.SqlFilePath, request.StoredProcedure);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            UPDATE {SourceTable}
            SET ReportId = @ReportId,
                SourceType = @SourceType,
                SqlConnectionName = @SqlConnectionName,
                SqlDatabase = @SqlDatabase,
                SqlText = @SqlText,
                SqlFilePath = @SqlFilePath,
                StoredProcedure = @StoredProcedure,
                TableTitle = @TableTitle,
                IncludeHtmlTable = @IncludeHtmlTable,
                AttachExcel = @AttachExcel,
                AttachCsv = @AttachCsv
            WHERE Id = @Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", request.Id);
        command.Parameters.AddWithValue("@ReportId", request.ReportId);
        Add(command, "@SourceType", sourceType, 40);
        Add(command, "@SqlConnectionName", request.SqlConnectionName, 128);
        Add(command, "@SqlDatabase", request.SqlDatabase, 128);
        Add(command, "@SqlText", sourceType == "INLINE_SQL" ? request.SqlText : string.Empty, -1);
        Add(command, "@SqlFilePath", sourceType == "SQL_FILE" ? request.SqlFilePath : string.Empty, 2048);
        Add(command, "@StoredProcedure", sourceType == "STORED_PROCEDURE" ? request.StoredProcedure : string.Empty, 512);
        Add(command, "@TableTitle", request.TableTitle, 260);
        command.Parameters.AddWithValue("@IncludeHtmlTable", request.IncludeHtmlTable);
        command.Parameters.AddWithValue("@AttachExcel", request.AttachExcel);
        command.Parameters.AddWithValue("@AttachCsv", request.AttachCsv);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("SQL saltinis nerastas.");
        }
    }

    public async Task DeleteSourceAsync(long sourceId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"DELETE FROM {SourceTable} WHERE Id = @Id;", connection);
        command.Parameters.AddWithValue("@Id", sourceId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("SQL saltinis nerastas.");
        }
    }

    public async Task<long> CreateRecipientAsync(IrisCreateRecipientRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (request.ReportId <= 0)
        {
            throw new InvalidOperationException("Pasirinkite KPI taska.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nurodykite teisinga el. pasto adresa.");
        }

        var addressType = NormalizeAddressType(request.AddressType);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            INSERT INTO {RecipientTable}
                (ReportId, AddressType, Email, DisplayName, Erp, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES
                (@ReportId, @AddressType, @Email, @DisplayName, @Erp, @CreatedBy);
            """, connection);
        command.Parameters.AddWithValue("@ReportId", request.ReportId);
        Add(command, "@AddressType", addressType, 10);
        Add(command, "@Email", request.Email, 320);
        Add(command, "@DisplayName", request.DisplayName, 260);
        Add(command, "@Erp", request.Erp, 100);
        Add(command, "@CreatedBy", request.CreatedBy, 260);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task SetRecipientActiveAsync(long recipientId, bool isActive, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            UPDATE {RecipientTable}
            SET IsActive = @IsActive
            WHERE Id = @Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", recipientId);
        command.Parameters.AddWithValue("@IsActive", isActive);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Adresatas nerastas.");
        }
    }

    public async Task<long> CreateScheduleAsync(IrisCreateScheduleRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (request.ReportId <= 0)
        {
            throw new InvalidOperationException("Pasirinkite KPI taska.");
        }

        var scheduleType = NormalizeScheduleType(request.ScheduleType);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            INSERT INTO {ScheduleTable}
                (ReportId, ScheduleType, ScheduleJson, NextRunAt, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES
                (@ReportId, @ScheduleType, @ScheduleJson, @NextRunAt, @CreatedBy);
            """, connection);
        command.Parameters.AddWithValue("@ReportId", request.ReportId);
        Add(command, "@ScheduleType", scheduleType, 40);
        Add(command, "@ScheduleJson", request.ScheduleJson, -1);
        command.Parameters.AddWithValue("@NextRunAt", request.NextRunAt.HasValue ? request.NextRunAt.Value : DBNull.Value);
        Add(command, "@CreatedBy", request.CreatedBy, 260);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateScheduleAsync(IrisUpdateScheduleRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (request.Id <= 0)
        {
            throw new InvalidOperationException("Pasirinkite grafika redagavimui.");
        }

        var scheduleType = NormalizeScheduleType(request.ScheduleType);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            UPDATE {ScheduleTable}
            SET ReportId = @ReportId,
                ScheduleType = @ScheduleType,
                ScheduleJson = @ScheduleJson,
                NextRunAt = @NextRunAt
            WHERE Id = @Id;
            """, connection);
        command.Parameters.AddWithValue("@Id", request.Id);
        command.Parameters.AddWithValue("@ReportId", request.ReportId);
        Add(command, "@ScheduleType", scheduleType, 40);
        Add(command, "@ScheduleJson", request.ScheduleJson, -1);
        command.Parameters.AddWithValue("@NextRunAt", request.NextRunAt.HasValue ? request.NextRunAt.Value : DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Grafikas nerastas.");
        }
    }

    public async Task SetScheduleActiveAsync(long scheduleId, bool isActive, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"UPDATE {ScheduleTable} SET IsActive = @IsActive WHERE Id = @Id;", connection);
        command.Parameters.AddWithValue("@Id", scheduleId);
        command.Parameters.AddWithValue("@IsActive", isActive);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Grafikas nerastas.");
        }
    }

    public async Task DeleteScheduleAsync(long scheduleId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"DELETE FROM {ScheduleTable} WHERE Id = @Id;", connection);
        command.Parameters.AddWithValue("@Id", scheduleId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Grafikas nerastas.");
        }
    }

    private async Task<int> GetNextSourceOrderAsync(SqlConnection connection, long reportId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand($"SELECT ISNULL(MAX(SourceOrder), 0) + 1 FROM {SourceTable} WHERE ReportId = @ReportId;", connection);
        command.Parameters.AddWithValue("@ReportId", reportId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> HasColumnAsync(SqlConnection connection, string normalizedTableName, string columnName, CancellationToken cancellationToken)
    {
        var (_, schema, table) = SplitTableName(normalizedTableName);
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema
              AND TABLE_NAME = @Table
              AND COLUMN_NAME = @Column;
            """, connection);
        Add(command, "@Schema", schema, 128);
        Add(command, "@Table", table, 128);
        Add(command, "@Column", columnName, 128);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static (string? Database, string Schema, string Table) SplitTableName(string normalizedName)
    {
        var parts = normalizedName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim('[', ']'))
            .ToArray();

        return parts.Length switch
        {
            3 => (parts[0], parts[1], parts[2]),
            2 => (null, parts[0], parts[1]),
            1 => (null, "dbo", parts[0]),
            _ => throw new InvalidOperationException($"Netinkamas SQL lenteles pavadinimas: {normalizedName}")
        };
    }

    private static string NormalizeSourceType(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "SQL_FILE" => "SQL_FILE",
            "STORED_PROCEDURE" => "STORED_PROCEDURE",
            _ => "INLINE_SQL"
        };

    private static string NormalizeAddressType(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "CC" => "CC",
            "BCC" => "BCC",
            _ => "TO"
        };

    private static string NormalizeScheduleType(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "ONCE" => "ONCE",
            "DAILY" => "DAILY",
            "WEEKLY" => "WEEKLY",
            "MONTHLY" => "MONTHLY",
            _ => "MANUAL"
        };

    private static void ValidateSource(string sourceType, long reportId, string sqlConnectionName, string sqlText, string sqlFilePath, string storedProcedure)
    {
        if (reportId <= 0)
        {
            throw new InvalidOperationException("Pasirinkite KPI taska.");
        }

        if (string.IsNullOrWhiteSpace(sqlConnectionName))
        {
            throw new InvalidOperationException("Nurodykite SQL serverio profili.");
        }

        if (sourceType == "INLINE_SQL" && string.IsNullOrWhiteSpace(sqlText))
        {
            throw new InvalidOperationException("Inline SQL saltiniui privalomas SQL tekstas.");
        }

        if (sourceType == "SQL_FILE" && string.IsNullOrWhiteSpace(sqlFilePath))
        {
            throw new InvalidOperationException("SQL failo saltiniui privalomas failo kelias.");
        }

        if (sourceType == "STORED_PROCEDURE" && string.IsNullOrWhiteSpace(storedProcedure))
        {
            throw new InvalidOperationException("Stored procedure saltiniui privalomas proceduros pavadinimas.");
        }
    }

    private IEnumerable<string> BuildSchemaScripts()
    {
        yield return $"""
            IF OBJECT_ID(N'{ObjectNameLiteral(ReportTable)}', N'U') IS NULL
            BEGIN
                CREATE TABLE {ReportTable} (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IRIS_REPORTS PRIMARY KEY,
                    KpiName NVARCHAR(260) NOT NULL,
                    Title NVARCHAR(500) NOT NULL,
                    SubjectTemplate NVARCHAR(500) NOT NULL CONSTRAINT DF_IRIS_REPORTS_SubjectTemplate DEFAULT (N''),
                    SenderKey NVARCHAR(80) NOT NULL,
                    DefaultSqlConnectionName NVARCHAR(128) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_IRIS_REPORTS_IsActive DEFAULT (1),
                    CreatedBy NVARCHAR(260) NOT NULL,
                    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_IRIS_REPORTS_CreatedAt DEFAULT (SYSDATETIME()),
                    UpdatedAt DATETIME2(0) NULL,
                    CONSTRAINT UQ_IRIS_REPORTS_KpiName UNIQUE (KpiName)
                );
            END;
            """;
        yield return $"""
            IF COL_LENGTH(N'{ObjectNameLiteral(ReportTable)}', N'SubjectTemplate') IS NULL
            BEGIN
                ALTER TABLE {ReportTable}
                ADD SubjectTemplate NVARCHAR(500) NOT NULL CONSTRAINT DF_IRIS_REPORTS_SubjectTemplate DEFAULT (N'');
            END;

            IF COL_LENGTH(N'{ObjectNameLiteral(ReportTable)}', N'SubjectTemplate') IS NOT NULL
            BEGIN
                EXEC(N'{SqlLiteral($"UPDATE {ReportTable} SET SubjectTemplate = Title WHERE SubjectTemplate = N'';")}')
            END;
            """;
        yield return $"""
            IF OBJECT_ID(N'{ObjectNameLiteral(SourceTable)}', N'U') IS NULL
            BEGIN
                CREATE TABLE {SourceTable} (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IRIS_REPORT_SOURCES PRIMARY KEY,
                    ReportId BIGINT NOT NULL,
                    SourceOrder INT NOT NULL,
                    SourceType NVARCHAR(40) NOT NULL,
                    SqlConnectionName NVARCHAR(128) NOT NULL,
                    SqlDatabase NVARCHAR(128) NOT NULL CONSTRAINT DF_IRIS_SOURCES_Db DEFAULT (N''),
                    SqlText NVARCHAR(MAX) NOT NULL CONSTRAINT DF_IRIS_SOURCES_SqlText DEFAULT (N''),
                    SqlFilePath NVARCHAR(2048) NOT NULL CONSTRAINT DF_IRIS_SOURCES_File DEFAULT (N''),
                    StoredProcedure NVARCHAR(512) NOT NULL CONSTRAINT DF_IRIS_SOURCES_Proc DEFAULT (N''),
                    TableTitle NVARCHAR(260) NOT NULL CONSTRAINT DF_IRIS_SOURCES_Title DEFAULT (N''),
                    IncludeHtmlTable BIT NOT NULL CONSTRAINT DF_IRIS_SOURCES_IncludeHtml DEFAULT (1),
                    AttachExcel BIT NOT NULL CONSTRAINT DF_IRIS_SOURCES_AttachExcel DEFAULT (0),
                    AttachCsv BIT NOT NULL CONSTRAINT DF_IRIS_SOURCES_AttachCsv DEFAULT (0),
                    CreatedBy NVARCHAR(260) NOT NULL,
                    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_IRIS_SOURCES_CreatedAt DEFAULT (SYSDATETIME()),
                    CONSTRAINT FK_IRIS_SOURCES_REPORT FOREIGN KEY (ReportId) REFERENCES {ReportTable}(Id)
                );
            END;
            """;
        yield return $"""
            IF OBJECT_ID(N'{ObjectNameLiteral(RecipientTable)}', N'U') IS NULL
            BEGIN
                CREATE TABLE {RecipientTable} (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IRIS_REPORT_RECIPIENTS PRIMARY KEY,
                    ReportId BIGINT NOT NULL,
                    AddressType NVARCHAR(10) NOT NULL CONSTRAINT DF_IRIS_RECIPIENTS_Type DEFAULT (N'TO'),
                    Email NVARCHAR(320) NOT NULL,
                    DisplayName NVARCHAR(260) NOT NULL CONSTRAINT DF_IRIS_RECIPIENTS_Name DEFAULT (N''),
                    Erp NVARCHAR(100) NOT NULL CONSTRAINT DF_IRIS_RECIPIENTS_Erp DEFAULT (N''),
                    IsActive BIT NOT NULL CONSTRAINT DF_IRIS_RECIPIENTS_Active DEFAULT (1),
                    CreatedBy NVARCHAR(260) NOT NULL,
                    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_IRIS_RECIPIENTS_CreatedAt DEFAULT (SYSDATETIME()),
                    CONSTRAINT FK_IRIS_RECIPIENTS_REPORT FOREIGN KEY (ReportId) REFERENCES {ReportTable}(Id)
                );
            END;
            """;
        yield return $"""
            IF OBJECT_ID(N'{ObjectNameLiteral(ScheduleTable)}', N'U') IS NULL
            BEGIN
                CREATE TABLE {ScheduleTable} (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IRIS_SCHEDULES PRIMARY KEY,
                    ReportId BIGINT NOT NULL,
                    ScheduleType NVARCHAR(40) NOT NULL,
                    ScheduleJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_IRIS_SCHEDULES_Json DEFAULT (N''),
                    NextRunAt DATETIME2(0) NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_IRIS_SCHEDULES_Active DEFAULT (1),
                    CreatedBy NVARCHAR(260) NOT NULL,
                    CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_IRIS_SCHEDULES_CreatedAt DEFAULT (SYSDATETIME()),
                    CONSTRAINT FK_IRIS_SCHEDULES_REPORT FOREIGN KEY (ReportId) REFERENCES {ReportTable}(Id)
                );
            END;
            """;
        yield return $"""
            IF OBJECT_ID(N'{ObjectNameLiteral(RunTable)}', N'U') IS NULL
            BEGIN
                CREATE TABLE {RunTable} (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IRIS_RUNS PRIMARY KEY,
                    ReportId BIGINT NOT NULL,
                    MessageId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_IRIS_RUNS_MessageId DEFAULT (NEWID()),
                    Mode NVARCHAR(20) NOT NULL,
                    Status NVARCHAR(40) NOT NULL,
                    EmlPath NVARCHAR(2048) NOT NULL CONSTRAINT DF_IRIS_RUNS_EmlPath DEFAULT (N''),
                    ErrorMessage NVARCHAR(MAX) NOT NULL CONSTRAINT DF_IRIS_RUNS_Error DEFAULT (N''),
                    QueuedAt DATETIME2(0) NOT NULL CONSTRAINT DF_IRIS_RUNS_QueuedAt DEFAULT (SYSDATETIME()),
                    StartedAt DATETIME2(0) NULL,
                    FinishedAt DATETIME2(0) NULL,
                    CreatedBy NVARCHAR(260) NOT NULL CONSTRAINT DF_IRIS_RUNS_CreatedBy DEFAULT (N''),
                    CONSTRAINT FK_IRIS_RUNS_REPORT FOREIGN KEY (ReportId) REFERENCES {ReportTable}(Id)
                );
            END;
            """;
    }

    private static string ObjectNameLiteral(string normalizedName) =>
        normalizedName.Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

    private static string SqlLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private string GetConnectionString() =>
        configuration.GetConnectionString(_options.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{_options.ConnectionStringName}' nerastas.");

    private static void Add(SqlCommand command, string name, string? value, int size)
    {
        var parameter = size == -1
            ? command.Parameters.Add(name, System.Data.SqlDbType.NVarChar, -1)
            : command.Parameters.Add(name, System.Data.SqlDbType.NVarChar, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
