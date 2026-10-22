using System.Data;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public interface IIrisPreviewAuditStore
{
    Task<Guid> BeginAsync(IrisSourcePreviewRequest request, string actor, CancellationToken ct);
    Task PrepareAsync(Guid id, string sql, CancellationToken ct);
    Task ExecutionAsync(Guid id, string sql, CancellationToken ct);
    Task CompleteAsync(Guid id, string status, int rows, long elapsedMs, string? database, string error);
}

public sealed class IrisPreviewAuditStore(IConfiguration configuration, IOptions<IrisOptions> options,
    ILogger<IrisPreviewAuditStore> logger) : IIrisPreviewAuditStore
{
    private string Table => SqlName.NormalizeTableName(options.Value.PreviewLogTableName,
        "BLS_LOGIT_STAT.dbo.IRIS_QUERY_PREVIEW_LOG");

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(configuration.GetConnectionString(options.Value.ConnectionStringName));
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<Guid> BeginAsync(IrisSourcePreviewRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256)
            throw new InvalidOperationException("Iris.Preview.IdentityRequired");
        try
        {
            await using var connection = await OpenAsync(ct);
            await using (var schema = new SqlCommand($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @LockResult int;
                EXEC @LockResult = sys.sp_getapplock @Resource=N'IRIS_PREVIEW_LOG_SCHEMA_V1',
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @LockResult < 0 THROW 51000, 'IRIS preview audit schema lock timeout.', 1;
                IF OBJECT_ID(N'{Table}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {Table} (
                        Id uniqueidentifier NOT NULL PRIMARY KEY,
                        StartedAt datetime2(7) NOT NULL,
                        CompletedAt datetime2(7) NULL,
                        Actor nvarchar(256) NOT NULL,
                        SourceId bigint NOT NULL,
                        ReportId bigint NOT NULL,
                        SourceType nvarchar(40) NOT NULL,
                        ConnectionName nvarchar(128) NOT NULL,
                        DatabaseName nvarchar(128) NOT NULL,
                        SqlText nvarchar(max) NOT NULL,
                        ExecutedSql nvarchar(max) NOT NULL DEFAULT (N''),
                        SqlFilePath nvarchar(2048) NOT NULL,
                        ProcedureName nvarchar(512) NOT NULL,
                        Status nvarchar(20) NOT NULL,
                        RowsShown int NOT NULL,
                        DurationMs bigint NULL,
                        ErrorMessage nvarchar(4000) NOT NULL
                    );
                    CREATE INDEX IX_IRIS_PREVIEW_LOG_Started ON {Table} (StartedAt DESC);
                END;
                IF COL_LENGTH(N'{Table}', N'ExecutedSql') IS NULL
                    ALTER TABLE {Table} ADD ExecutedSql nvarchar(max) NOT NULL DEFAULT (N'') WITH VALUES;
                COMMIT TRANSACTION;
                """, connection)) await schema.ExecuteNonQueryAsync(ct);
            var id = Guid.NewGuid();
            await using var command = new SqlCommand($"""
                INSERT INTO {Table} (Id, StartedAt, Actor, SourceId, ReportId, SourceType,
                    ConnectionName, DatabaseName, SqlText, SqlFilePath, ProcedureName, Status, RowsShown, ErrorMessage)
                VALUES (@Id, SYSUTCDATETIME(), @Actor, @SourceId, @ReportId, @Type,
                    @Connection, @Database, @Sql, @File, @Procedure, N'STARTED', 0, N'');
                """, connection);
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            command.Parameters.Add("@SourceId", SqlDbType.BigInt).Value = Math.Max(0, request.SourceId);
            command.Parameters.Add("@ReportId", SqlDbType.BigInt).Value = Math.Max(0, request.ReportId);
            Add(command, "@Actor", actor, 256); Add(command, "@Type", request.SourceType, 40);
            Add(command, "@Connection", request.ConnectionName, 128); Add(command, "@Database", request.Database, 128);
            Add(command, "@Sql", Cut(request.SqlText, 1_000_000), -1); Add(command, "@File", request.FilePath, 2048);
            Add(command, "@Procedure", request.Procedure, 512);
            await command.ExecuteNonQueryAsync(ct);
            return id;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "IRIS preview audit could not start; SQL source execution denied.");
            throw new InvalidOperationException("Iris.Preview.AuditUnavailable", exception);
        }
    }

    public async Task PrepareAsync(Guid id, string sql, CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new SqlCommand($"UPDATE {Table} SET SqlText=@Sql WHERE Id=@Id AND Status=N'STARTED';", connection);
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            Add(command, "@Sql", sql, -1);
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Audit entry missing.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new InvalidOperationException("Iris.Preview.AuditUnavailable", exception); }
    }

    public async Task ExecutionAsync(Guid id, string sql, CancellationToken ct)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = new SqlCommand($"UPDATE {Table} SET ExecutedSql=@Sql WHERE Id=@Id AND Status=N'STARTED';", connection);
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            Add(command, "@Sql", sql, -1);
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Audit entry missing.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new InvalidOperationException("Iris.Preview.AuditUnavailable", exception); }
    }

    public async Task CompleteAsync(Guid id, string status, int rows, long elapsedMs, string? database, string error)
    {
        try
        {
            await using var connection = await OpenAsync(CancellationToken.None);
            await using var command = new SqlCommand($"""
                UPDATE {Table} SET CompletedAt=SYSUTCDATETIME(), Status=@Status, RowsShown=@Rows,
                    DurationMs=@Duration, ErrorMessage=@Error,
                    DatabaseName=COALESCE(@Database, DatabaseName) WHERE Id=@Id;
                """, connection) { CommandTimeout = 15 };
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            command.Parameters.Add("@Rows", SqlDbType.Int).Value = rows;
            command.Parameters.Add("@Duration", SqlDbType.BigInt).Value = elapsedMs;
            Add(command, "@Status", status, 20); Add(command, "@Error", error, 4000);
            command.Parameters.Add("@Database", SqlDbType.NVarChar, 128).Value = (object?)database ?? DBNull.Value;
            if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Audit entry missing.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "IRIS preview audit {AuditId} could not complete. Status {Status}.", id, status);
            throw new InvalidOperationException("Iris.Preview.AuditFinishFailed", exception);
        }
    }

    private static string Cut(string? value, int limit) => value is null ? "" : value[..Math.Min(value.Length, limit)];
    private static void Add(SqlCommand command, string name, string? value, int length) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, length).Value = length == -1 ? value ?? "" : Cut(value, length);
}
