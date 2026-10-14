using System.Data;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class DataExchangeService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IOptions<DataExchangeOptions> options,
    ILogger<DataExchangeService> logger) : IDataExchangeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly DataExchangeOptions _options = options.Value;

    public async Task<IReadOnlyList<ExchangeRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (UseSqlServer())
        {
            return await ListFromSqlAsync(cancellationToken);
        }

        return await ListFromLocalStorageAsync(cancellationToken);
    }

    public async Task<ExchangeRecord> SaveAsync(
        IFormFile file,
        string uploadedBy,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        ValidateUpload(file);

        if (UseSqlServer())
        {
            return await SaveToSqlAsync(file, uploadedBy, notes, cancellationToken);
        }

        return await SaveToLocalStorageAsync(file, uploadedBy, notes, cancellationToken);
    }

    public async Task<ExchangeDownload?> GetDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (UseSqlServer())
        {
            return await GetSqlDownloadAsync(id, cancellationToken);
        }

        return await GetLocalDownloadAsync(id, cancellationToken);
    }

    private bool UseSqlServer()
    {
        var connectionString = configuration.GetConnectionString(_options.ConnectionStringName);
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private async Task<IReadOnlyList<ExchangeRecord>> ListFromSqlAsync(CancellationToken cancellationToken)
    {
        var tableName = SqlName.NormalizeTableName(
            _options.UploadTableName,
            "dbo.DataExchangeFiles");

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (100)
                Id,
                FileName,
                ContentType,
                FileSize,
                UploadedBy,
                UploadedAt,
                Status,
                Notes
            FROM {tableName}
            ORDER BY UploadedAt DESC
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<ExchangeRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ExchangeRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "application/octet-stream" : reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetDateTimeOffset(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return records;
    }

    private async Task<ExchangeRecord> SaveToSqlAsync(
        IFormFile file,
        string uploadedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        var tableName = SqlName.NormalizeTableName(
            _options.UploadTableName,
            "dbo.DataExchangeFiles");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var record = CreateRecord(file, uploadedBy, notes);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {tableName}
                (Id, FileName, ContentType, FileSize, UploadedBy, UploadedAt, Status, Notes, Content)
            VALUES
                (@Id, @FileName, @ContentType, @FileSize, @UploadedBy, @UploadedAt, @Status, @Notes, @Content)
            """;

        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = record.Id;
        command.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = record.FileName;
        command.Parameters.Add("@ContentType", SqlDbType.NVarChar, 200).Value = record.ContentType;
        command.Parameters.Add("@FileSize", SqlDbType.BigInt).Value = record.FileSize;
        command.Parameters.Add("@UploadedBy", SqlDbType.NVarChar, 256).Value = record.UploadedBy;
        command.Parameters.Add("@UploadedAt", SqlDbType.DateTimeOffset).Value = record.UploadedAt;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = record.Status;
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = (object?)record.Notes ?? DBNull.Value;
        command.Parameters.Add("@Content", SqlDbType.VarBinary, -1).Value = buffer.ToArray();

        await command.ExecuteNonQueryAsync(cancellationToken);
        return record;
    }

    private async Task<ExchangeDownload?> GetSqlDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var tableName = SqlName.NormalizeTableName(
            _options.UploadTableName,
            "dbo.DataExchangeFiles");

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                FileName,
                ContentType,
                Content
            FROM {tableName}
            WHERE Id = @Id
            """;
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var fileName = reader.GetString(0);
        var contentType = reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1);
        var content = (byte[])reader["Content"];

        return new ExchangeDownload(fileName, contentType, new MemoryStream(content, writable: false));
    }

    private async Task<IReadOnlyList<ExchangeRecord>> ListFromLocalStorageAsync(CancellationToken cancellationToken)
    {
        var root = GetStorageRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        var records = new List<ExchangeRecord>();
        foreach (var metadataPath in Directory.EnumerateFiles(root, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var record = await JsonSerializer.DeserializeAsync<ExchangeRecord>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Nepavyko perskaityti Data Exchange metaduomenu: {Path}", metadataPath);
            }
        }

        return records
            .OrderByDescending(record => record.UploadedAt)
            .Take(100)
            .ToArray();
    }

    private async Task<ExchangeRecord> SaveToLocalStorageAsync(
        IFormFile file,
        string uploadedBy,
        string? notes,
        CancellationToken cancellationToken)
    {
        var root = EnsureStorageRoot();
        var record = CreateRecord(file, uploadedBy, notes);

        await using (var fileStream = File.Create(GetContentPath(root, record.Id)))
        {
            await file.CopyToAsync(fileStream, cancellationToken);
        }

        await using (var metadataStream = File.Create(GetMetadataPath(root, record.Id)))
        {
            await JsonSerializer.SerializeAsync(metadataStream, record, JsonOptions, cancellationToken);
        }

        return record;
    }

    private async Task<ExchangeDownload?> GetLocalDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var root = GetStorageRoot();
        var metadataPath = GetMetadataPath(root, id);
        var contentPath = GetContentPath(root, id);

        if (!File.Exists(metadataPath) || !File.Exists(contentPath))
        {
            return null;
        }

        await using var metadataStream = File.OpenRead(metadataPath);
        var record = await JsonSerializer.DeserializeAsync<ExchangeRecord>(
            metadataStream,
            JsonOptions,
            cancellationToken);

        if (record is null)
        {
            return null;
        }

        return new ExchangeDownload(
            record.FileName,
            string.IsNullOrWhiteSpace(record.ContentType) ? "application/octet-stream" : record.ContentType,
            File.OpenRead(contentPath));
    }

    private ExchangeRecord CreateRecord(IFormFile file, string uploadedBy, string? notes)
    {
        return new ExchangeRecord(
            Guid.NewGuid(),
            Path.GetFileName(file.FileName),
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            file.Length,
            uploadedBy,
            DateTimeOffset.UtcNow,
            "Ikeltas",
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    private void ValidateUpload(IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Pasirinktas failas tuscias.");
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"Failas virsija {_options.MaxFileSizeMb} MB limita.");
        }
    }

    private string GetConnectionString()
    {
        return configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException("SQL Server prisijungimo eilute nesukonfiguruota.");
    }

    private string EnsureStorageRoot()
    {
        var root = GetStorageRoot();
        Directory.CreateDirectory(root);
        return root;
    }

    private string GetStorageRoot()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(environment.ContentRootPath, _options.StoragePath);
    }

    private static string GetMetadataPath(string root, Guid id) => Path.Combine(root, $"{id:N}.json");

    private static string GetContentPath(string root, Guid id) => Path.Combine(root, $"{id:N}.bin");
}
