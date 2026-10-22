using System.Data;
using System.Security.Claims;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;

namespace DATA_EXCHANGE_HUB.Services;

public sealed partial class DikeAgreementStore
{
    public const int DocumentMaxBytes = 10 * 1024 * 1024;

    public async Task<IReadOnlyList<DikeDocument>> ListDocumentsAsync(Guid agreementId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT Id, AgreementId, FileName, Note, ContentType, CONVERT(bigint, DATALENGTH(FileData)), UploadedAt, UploadedBy FROM {DocumentTable} WHERE AgreementId = @AgreementId AND IsDeleted = 0 ORDER BY UploadedAt DESC, Id;", connection);
        command.Parameters.Add("@AgreementId", SqlDbType.UniqueIdentifier).Value = agreementId;
        var result = new List<DikeDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadDocument(reader));
        return result;
    }

    public async Task<DikeDocumentContent?> GetDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT Id, AgreementId, FileName, Note, ContentType, CONVERT(bigint, DATALENGTH(FileData)), UploadedAt, UploadedBy, FileData FROM {DocumentTable} WHERE Id = @Id AND IsDeleted = 0;", connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(ReadDocument(reader), (byte[])reader.GetValue(8)) : null;
    }

    public async Task UploadDocumentAsync(Guid agreementId, IFormFile file, string? note, string actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256) throw new InvalidOperationException("Dike.Validation.Actor");
        if (file.Length < 1 || file.Length > DocumentMaxBytes) throw new InvalidOperationException("Dike.Document.SizeError");
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.Length > 255 || name.Any(char.IsControl) || extension is not (".pdf" or ".eml")) throw new InvalidOperationException("Dike.Document.TypeError");
        if ((note?.Length ?? 0) > 500) throw new InvalidOperationException("Dike.Document.NoteError");
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > DocumentMaxBytes) throw new InvalidOperationException("Dike.Document.SizeError");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length == 0) throw new InvalidOperationException("Dike.Document.SizeError");
        if (extension == ".pdf" && !bytes.AsSpan(0, Math.Min(bytes.Length, 5)).SequenceEqual("%PDF-"u8))
            throw new InvalidOperationException("Dike.Document.InvalidContent");
        if (extension == ".eml") _ = DikeMailReader.Read(bytes);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var insert = new SqlCommand($"""
            IF NOT EXISTS (SELECT 1 FROM {ConfigTable} WHERE Id = @AgreementId) THROW 51000, 'Dike.Validation.NotFound', 1;
            INSERT INTO {DocumentTable} (Id, AgreementId, FileName, Note, ContentType, FileData, UploadedAt, UploadedBy)
            VALUES (@Id, @AgreementId, @Name, @Note, @ContentType, @Data, @At, @Actor);
            """, connection, transaction);
        insert.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        insert.Parameters.Add("@AgreementId", SqlDbType.UniqueIdentifier).Value = agreementId;
        insert.Parameters.Add("@Name", SqlDbType.NVarChar, 255).Value = name;
        insert.Parameters.Add("@Note", SqlDbType.NVarChar, 500).Value = note?.Trim() ?? "";
        insert.Parameters.Add("@ContentType", SqlDbType.NVarChar, 100).Value = extension == ".pdf" ? "application/pdf" : "message/rfc822";
        insert.Parameters.Add("@Data", SqlDbType.VarBinary, -1).Value = bytes;
        insert.Parameters.Add("@At", SqlDbType.DateTime2).Value = now;
        insert.Parameters.Add("@Actor", SqlDbType.NVarChar, 256).Value = actor;
        await insert.ExecuteNonQueryAsync(cancellationToken);
        var snapshot = JsonSerializer.Serialize(new { Document = id, FileName = name, FileNote = note?.Trim() ?? "", FileStatus = "Dike.Document.Status.Active" });
        await LogDocumentAsync(connection, transaction, agreementId, "FileUploaded", null, snapshot, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> DeleteDocumentAsync(Guid id, ClaimsPrincipal user, string actor, CancellationToken cancellationToken)
    {
        if (!await accessService.IsAdminAsync(user, cancellationToken)) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256) throw new InvalidOperationException("Dike.Validation.Actor");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        Guid agreementId;
        string name, note;
        await using (var read = new SqlCommand($"SELECT AgreementId, FileName, Note FROM {DocumentTable} WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND IsDeleted = 0;", connection, transaction))
        {
            read.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Dike.Document.NotFound");
            agreementId = reader.GetGuid(0); name = reader.GetString(1); note = reader.GetString(2);
        }
        var now = DateTime.UtcNow;
        await using var update = new SqlCommand($"UPDATE {DocumentTable} SET IsDeleted = 1, DeletedAt = @At, DeletedBy = @Actor WHERE Id = @Id;", connection, transaction);
        update.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        update.Parameters.Add("@At", SqlDbType.DateTime2).Value = now;
        update.Parameters.Add("@Actor", SqlDbType.NVarChar, 256).Value = actor;
        await update.ExecuteNonQueryAsync(cancellationToken);
        var before = JsonSerializer.Serialize(new { Document = id, FileName = name, FileNote = note, FileStatus = "Dike.Document.Status.Active" });
        var after = JsonSerializer.Serialize(new { Document = id, FileName = name, FileNote = note, FileStatus = "Dike.Document.Status.Deleted" });
        await LogDocumentAsync(connection, transaction, agreementId, "FileDeleted", before, after, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return agreementId;
    }

    private async Task LogDocumentAsync(SqlConnection connection, SqlTransaction transaction, Guid agreementId,
        string action, string? before, string after, string actor, DateTime now, CancellationToken cancellationToken)
    {
        await using var log = new SqlCommand($"INSERT INTO {LogTable} (AgreementId, OwnerId, ClientName, OccurredAt, Actor, Action, BeforeJson, AfterJson) SELECT Id, OwnerId, ClientName, @At, @Actor, @Action, @Before, @After FROM {ConfigTable} WHERE Id = @Id;", connection, transaction);
        log.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = agreementId;
        log.Parameters.Add("@At", SqlDbType.DateTime2).Value = now;
        log.Parameters.Add("@Actor", SqlDbType.NVarChar, 256).Value = actor;
        log.Parameters.Add("@Action", SqlDbType.NVarChar, 20).Value = action;
        log.Parameters.Add("@Before", SqlDbType.NVarChar, -1).Value = (object?)before ?? DBNull.Value;
        log.Parameters.Add("@After", SqlDbType.NVarChar, -1).Value = after;
        if (await log.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Dike.Validation.NotFound");
    }

    private static DikeDocument ReadDocument(SqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetInt64(5), DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc), reader.GetString(7));
}
