using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed partial class DikeAgreementStore(IConfiguration configuration, IOptions<DikeOptions> options, IDikeAccessService accessService)
{
    private string ConfigTable => SqlName.NormalizeTableName(options.Value.ConfigTableName, "BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_CONFIG");
    private string LogTable => SqlName.NormalizeTableName(options.Value.LogTableName, "BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_LOG");
    private string DocumentTable => SqlName.NormalizeTableName(options.Value.DocumentTableName, "BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_DOCUMENTS");

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(configuration.GetConnectionString(options.Value.AccessConnectionStringName)
            ?? throw new InvalidOperationException("DIKE SQL connection is not configured."));
        try
        {
            await connection.OpenAsync(cancellationToken);
            // Schema changes are additive only. An incompatible existing table is never overwritten.
            await using var command = new SqlCommand($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @LockResult int;
                EXEC @LockResult = sys.sp_getapplock @Resource = N'DIKE_SCHEMA_V1', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @LockResult < 0 THROW 51000, 'DIKE schema lock timeout.', 1;
                IF OBJECT_ID(N'{ConfigTable}', N'U') IS NULL
                CREATE TABLE {ConfigTable} (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    OwnerId bigint NOT NULL,
                    ClientName nvarchar(400) NOT NULL,
                    DataJson nvarchar(max) NOT NULL,
                    Version int NOT NULL,
                    UpdatedAt datetime2(7) NOT NULL,
                    UpdatedBy nvarchar(256) NOT NULL
                );
                IF OBJECT_ID(N'{LogTable}', N'U') IS NULL
                CREATE TABLE {LogTable} (
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    AgreementId uniqueidentifier NOT NULL,
                    OwnerId bigint NOT NULL,
                    ClientName nvarchar(400) NOT NULL,
                    OccurredAt datetime2(7) NOT NULL,
                    Actor nvarchar(256) NOT NULL,
                    Action nvarchar(20) NOT NULL,
                    BeforeJson nvarchar(max) NULL,
                    AfterJson nvarchar(max) NOT NULL
                );
                IF OBJECT_ID(N'{DocumentTable}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {DocumentTable} (
                        Id uniqueidentifier NOT NULL PRIMARY KEY,
                        AgreementId uniqueidentifier NOT NULL,
                        FileName nvarchar(255) NOT NULL,
                        Note nvarchar(500) NOT NULL,
                        ContentType nvarchar(100) NOT NULL,
                        FileData varbinary(max) NOT NULL,
                        UploadedAt datetime2(7) NOT NULL,
                        UploadedBy nvarchar(256) NOT NULL,
                        IsDeleted bit NOT NULL DEFAULT (0),
                        DeletedAt datetime2(7) NULL,
                        DeletedBy nvarchar(256) NULL
                    );
                    CREATE INDEX IX_DikeDocuments_Agreement ON {DocumentTable} (AgreementId, IsDeleted, UploadedAt DESC);
                END;
                COMMIT TRANSACTION;
                """, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<DikeAgreement>> ListAsync(CancellationToken cancellationToken, long? ownerId = null)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var limit = ownerId.HasValue ? "" : "TOP (500)";
        await using var command = new SqlCommand($"SELECT {limit} DataJson, ClientName, UpdatedAt, UpdatedBy FROM {ConfigTable} WHERE (@OwnerId IS NULL OR OwnerId = @OwnerId) ORDER BY UpdatedAt DESC, Id;", connection);
        command.Parameters.Add("@OwnerId", SqlDbType.BigInt).Value = (object?)ownerId ?? DBNull.Value;
        var result = new List<DikeAgreement>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(JsonSerializer.Deserialize<DikeAgreementInput>(reader.GetString(0))!, reader.GetString(1),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc), reader.GetString(3)));
        return result;
    }

    public async Task<DikeAgreementInput?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT DataJson FROM {ConfigTable} WHERE Id = @Id;", connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<DikeAgreementInput>(json) : null;
    }

    public async Task<Guid> SaveAsync(DikeAgreementInput input, DikeClientInfo client, string actor, CancellationToken cancellationToken)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (input.OwnerId != client.OwnerId) throw new InvalidOperationException("Dike.Validation.Client");
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256) throw new InvalidOperationException("Dike.Validation.Actor");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string? before = null;
        var creating = input.Id == Guid.Empty;
        var id = creating ? Guid.NewGuid() : input.Id;
        if (!creating)
        {
            await using var read = new SqlCommand($"SELECT DataJson FROM {ConfigTable} WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND OwnerId = @OwnerId AND Version = @Version;", connection, transaction);
            read.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            read.Parameters.Add("@OwnerId", SqlDbType.BigInt).Value = input.OwnerId;
            read.Parameters.Add("@Version", SqlDbType.Int).Value = input.Version;
            before = await read.ExecuteScalarAsync(cancellationToken) as string;
            if (before is null) throw new InvalidOperationException("Dike.Validation.Conflict");
        }
        var saved = JsonSerializer.Deserialize<DikeAgreementInput>(JsonSerializer.Serialize(input))!;
        saved.Id = id;
        saved.Version = creating ? 1 : input.Version + 1;
        saved.ContractNo = saved.ContractNo.Trim();
        saved.FirstPeriod = saved.FirstPeriod.Replace("-", "");
        saved.Appendix = saved.Appendix.Trim();
        // Manager is no longer editable. Preserve legacy snapshots until the client SQL source is connected.
        saved.Manager = before is null ? "" : JsonSerializer.Deserialize<DikeAgreementInput>(before)!.Manager;
        saved.InfPrekCodes = saved.InfPrekCodes.Select(code => code.ToUpperInvariant()).Distinct().Order().ToList();
        saved.Recipients = string.Join("; ", DikeAgreementInput.Split(saved.Recipients));
        if (saved.AutomaticSending) saved.MonthDays = string.Join(", ", DikeAgreementInput.Split(saved.MonthDays).Select(int.Parse).Distinct().Order());
        var after = JsonSerializer.Serialize(saved);
        var now = DateTime.UtcNow;
        await using var write = new SqlCommand(creating
            ? $"INSERT INTO {ConfigTable} (Id, OwnerId, ClientName, DataJson, Version, UpdatedAt, UpdatedBy) VALUES (@Id, @OwnerId, @Client, @Json, @Version, @At, @Actor);"
            : $"UPDATE {ConfigTable} SET DataJson = @Json, Version = @Version, ClientName = @Client, UpdatedAt = @At, UpdatedBy = @Actor WHERE Id = @Id;", connection, transaction);
        write.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        write.Parameters.Add("@OwnerId", SqlDbType.BigInt).Value = input.OwnerId;
        write.Parameters.Add("@Client", SqlDbType.NVarChar, 400).Value = client.ShortName + " - " + client.Name;
        write.Parameters.Add("@Json", SqlDbType.NVarChar, -1).Value = after;
        write.Parameters.Add("@Version", SqlDbType.Int).Value = saved.Version;
        write.Parameters.Add("@At", SqlDbType.DateTime2).Value = now;
        write.Parameters.Add("@Actor", SqlDbType.NVarChar, 256).Value = actor;
        await write.ExecuteNonQueryAsync(cancellationToken);
        await using var log = new SqlCommand($"INSERT INTO {LogTable} (AgreementId, OwnerId, ClientName, OccurredAt, Actor, Action, BeforeJson, AfterJson) VALUES (@Id, @OwnerId, @Client, @At, @Actor, @Action, @Before, @After);", connection, transaction);
        log.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        log.Parameters.Add("@OwnerId", SqlDbType.BigInt).Value = input.OwnerId;
        log.Parameters.Add("@Client", SqlDbType.NVarChar, 400).Value = client.ShortName + " - " + client.Name;
        log.Parameters.Add("@At", SqlDbType.DateTime2).Value = now;
        log.Parameters.Add("@Actor", SqlDbType.NVarChar, 256).Value = actor;
        log.Parameters.Add("@Action", SqlDbType.NVarChar, 20).Value = creating ? "Created" : "Updated";
        log.Parameters.Add("@Before", SqlDbType.NVarChar, -1).Value = (object?)before ?? DBNull.Value;
        log.Parameters.Add("@After", SqlDbType.NVarChar, -1).Value = after;
        await log.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<DikeAudit>> HistoryAsync(long? ownerId, string search, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT TOP (200) Id, AgreementId, OwnerId, OccurredAt, Actor, Action, ClientName, BeforeJson, AfterJson FROM {LogTable} WHERE (@OwnerId IS NULL OR OwnerId = @OwnerId) AND (@Search = N'' OR CHARINDEX(@Search, CONCAT(Actor, N' ', ClientName, N' ', BeforeJson, N' ', AfterJson)) > 0) ORDER BY Id DESC;", connection);
        command.Parameters.Add("@OwnerId", SqlDbType.BigInt).Value = (object?)ownerId ?? DBNull.Value;
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = search.Trim();
        var result = new List<DikeAudit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0), reader.GetGuid(1), reader.GetInt64(2), DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), Changes(reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8)), ReadDocumentFileName(reader.GetString(8))));
        return result;
    }

    private static string? ReadDocumentFileName(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("FileName", out var name) ? name.GetString() : null;
    }

    public static IReadOnlyList<DikeChange> Changes(string? before, string after)
    {
        using var oldDocument = JsonDocument.Parse(before ?? "{}");
        using var newDocument = JsonDocument.Parse(after);
        var result = new List<DikeChange>();
        foreach (var property in newDocument.RootElement.EnumerateObject())
        {
            if (property.Name is "Id" or "Version" or "OwnerId") continue;
            var oldValue = oldDocument.RootElement.TryGetProperty(property.Name, out var value) ? Display(value) : "";
            var newValue = Display(property.Value);
            if (oldValue != newValue) result.Add(new(property.Name, oldValue, newValue));
        }
        return result;
    }

    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? string.Join(", ", value.EnumerateArray().Select(item => item.ToString()))
        : value.ToString();
}
