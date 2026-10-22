using System.Data;
using System.Diagnostics;
using System.Globalization;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public interface IIrisSourcePreviewService
{
    Task<IrisSourcePreviewResult> PreviewAsync(IrisSourcePreviewRequest request, string actor, CancellationToken cancellationToken);
}

public sealed class IrisSourcePreviewService(IConfiguration configuration,
    IOptions<IrisOptions> options, IWebHostEnvironment environment, IIrisPreviewAuditStore audit) : IIrisSourcePreviewService
{
    public async Task<IrisSourcePreviewResult> PreviewAsync(IrisSourcePreviewRequest request, string actor, CancellationToken cancellationToken)
    {
        var id = await audit.BeginAsync(request, actor, cancellationToken);
        var watch = Stopwatch.StartNew();
        var status = "FAILED";
        var error = "";
        string? database = null;
        var rowsShown = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.PreviewTimeoutSeconds, 1, 120)));
        var ct = deadline.Token;
        try
        {
            var type = request.SourceType?.Trim().ToUpperInvariant();
            var sql = type switch
            {
                "INLINE_SQL" => request.SqlText ?? "",
                "SQL_FILE" => await ReadSqlFileAsync(request.FilePath, ct),
                "STORED_PROCEDURE" => NormalizeProcedure(request.Procedure),
                _ => throw new InvalidOperationException("Iris.Preview.InvalidType")
            };
            // Parse the exact text that will be executed, including SQL files.
            // Procedures are an explicitly authorized, separate execution path.
            if (sql.Length > 1_000_000) throw new InvalidOperationException("Iris.Preview.InvalidSql");
            await audit.PrepareAsync(id, type == "STORED_PROCEDURE" ? "EXEC " + sql : sql, ct);
            if (type != "STORED_PROCEDURE") sql = IrisSelectPolicy.Prepare(sql);
            var profile = configuration.GetSection("ConnectionStrings").GetChildren()
                .FirstOrDefault(item => item.Key.Equals(request.ConnectionName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(profile?.Value)) throw new InvalidOperationException("Iris.Preview.InvalidConnection");
            var builder = new SqlConnectionStringBuilder(profile.Value);
            if (!string.IsNullOrWhiteSpace(request.Database))
            {
                if (request.Database.Trim().Length > 128) throw new InvalidOperationException("Iris.Preview.InvalidDatabase");
                builder.InitialCatalog = request.Database.Trim();
            }
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);
            database = connection.Database;
            if (type == "STORED_PROCEDURE")
            {
                var procedure = await ResolveProcedureAsync(connection, sql, ct);
                sql = procedure.Command;
                database = procedure.Database;
                await audit.PrepareAsync(id, "EXEC " + sql, ct);
            }
            await audit.ExecutionAsync(id, sql, ct);
            await using var command = new SqlCommand(sql, connection)
            {
                CommandType = type == "STORED_PROCEDURE" ? CommandType.StoredProcedure : CommandType.Text,
                CommandTimeout = Math.Clamp(options.Value.PreviewTimeoutSeconds, 1, 120)
            };
            using var cancel = ct.Register(command.Cancel);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var rowLimit = Math.Clamp(options.Value.PreviewMaxRows, 1, 2000);
            var tables = new List<IrisPreviewTable>();
            var budget = 1_000_000;
            var truncated = false;
            do
            {
                if (reader.FieldCount == 0) continue;
                if (tables.Count == 3 || budget <= 0) { truncated = true; command.Cancel(); break; }
                var columns = Enumerable.Range(0, Math.Min(reader.FieldCount, 100))
                    .Select(index => new IrisPreviewColumn(reader.GetName(index), reader.GetDataTypeName(index), true)).ToArray();
                var rows = new List<IReadOnlyList<string>>();
                var clipped = reader.FieldCount > columns.Length;
                var stop = false;
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count == rowLimit || budget <= 0) { clipped = true; stop = true; break; }
                    var values = new List<string>();
                    for (var index = 0; index < columns.Length; index++)
                    {
                        var value = Cell(reader, index, out var cellClipped);
                        clipped |= cellClipped;
                        if (value.Length > budget) { value = value[..Math.Max(0, budget)] + "…"; clipped = true; }
                        budget = Math.Max(0, budget - value.Length);
                        values.Add(value);
                    }
                    rows.Add(values);
                }
                rowsShown += rows.Count;
                tables.Add(new(columns, rows, clipped));
                if (stop) { truncated = true; command.Cancel(); break; }
            } while (await reader.NextResultAsync(ct));
            status = "SUCCESS";
            return new(tables, truncated, rowLimit, id);
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("Iris.Preview.", StringComparison.Ordinal))
        {
            status = "REJECTED"; error = exception.Message; throw;
        }
        catch (OperationCanceledException) { status = "CANCELLED"; error = "Iris.Preview.Timeout"; throw; }
        catch (SqlException exception) { error = $"SQL {exception.Number}: {exception.Message}"; throw; }
        catch (Exception exception) { error = exception.GetType().Name; throw; }
        finally { await audit.CompleteAsync(id, status, rowsShown, watch.ElapsedMilliseconds, database, error); }
    }

    private static string NormalizeProcedure(string? procedure)
    {
        if (string.IsNullOrWhiteSpace(procedure)) throw new InvalidOperationException("Iris.Preview.InvalidProcedure");
        try { return SqlName.NormalizeObjectName(procedure, "", maxParts: 3); }
        catch (InvalidOperationException) { throw new InvalidOperationException("Iris.Preview.InvalidProcedure"); }
    }

    private static async Task<(string Command, string Database)> ResolveProcedureAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        var parts = sql.Split('.').Select(part => part.Trim('[', ']')).ToArray();
        var database = parts.Length == 3 ? parts[0] : connection.Database;
        var schema = parts.Length >= 2 ? parts[^2] : "dbo";
        var name = parts[^1];
        if (new[] { "master", "msdb", "tempdb", "model" }.Contains(database, StringComparer.OrdinalIgnoreCase) ||
            schema.Equals("sys", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Iris.Preview.InvalidProcedure");
        var quotedDatabase = SqlName.QuoteIdentifier(database);
        await using var command = new SqlCommand($"""
            SELECT COUNT(*) FROM {quotedDatabase}.sys.procedures p
            JOIN {quotedDatabase}.sys.schemas s ON s.schema_id=p.schema_id
            WHERE s.name=@Schema AND p.name=@Name AND p.is_ms_shipped=0;
            """, connection) { CommandTimeout = 15 };
        command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = schema;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 128).Value = name;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
            throw new InvalidOperationException("Iris.Preview.InvalidProcedure");
        return ($"{quotedDatabase}.{SqlName.QuoteIdentifier(schema)}.{SqlName.QuoteIdentifier(name)}", database);
    }

    private static string Cell(SqlDataReader reader, int index, out bool clipped)
    {
        clipped = false;
        if (reader.IsDBNull(index)) return "NULL";
        if (reader.GetFieldType(index) == typeof(string))
        {
            using var text = reader.GetTextReader(index);
            var buffer = new char[2001];
            var length = text.ReadBlock(buffer, 0, buffer.Length);
            clipped = length > 2000;
            return new string(buffer, 0, Math.Min(length, 2000)) + (clipped ? "…" : "");
        }
        if (reader.GetFieldType(index) == typeof(byte[]))
        {
            var bytes = new byte[64];
            var length = reader.GetBytes(index, 0, bytes, 0, bytes.Length);
            clipped = reader.GetBytes(index, 0, null, 0, 0) > length;
            return "0x" + Convert.ToHexString(bytes.AsSpan(0, (int)length)) + (clipped ? "…" : "");
        }
        var value = reader.GetValue(index);
        var formatted = value is DateTime date ? date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
        clipped = formatted.Length > 2000;
        return clipped ? formatted[..2000] + "…" : formatted;
    }
    private async Task<string> ReadSqlFileAsync(string? filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new InvalidOperationException("Iris.Preview.InvalidFile");
        var path = Path.GetFullPath(filePath, environment.ContentRootPath);
        var roots = new[] { Path.Combine(environment.ContentRootPath, "Sql") }
            .Concat(options.Value.PreviewSqlRoots.Select(root => Path.GetFullPath(root, environment.ContentRootPath)));
        if (!path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || !roots.Any(root =>
            path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new InvalidOperationException("Iris.Preview.InvalidFile");
        for (FileSystemInfo? part = new FileInfo(path); part is not null;
             part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Iris.Preview.InvalidFile");
        await using var stream = File.OpenRead(path);
        if (stream.Length > 1_000_000) throw new InvalidOperationException("Iris.Preview.InvalidSql");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
