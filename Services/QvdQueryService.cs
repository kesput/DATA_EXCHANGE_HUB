using System.Data;
using System.Globalization;
using Bondski.QvdLib;
using DATA_EXCHANGE_HUB.Models;
using DuckDB.NET.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class QvdQueryService(IConfiguration configuration, IWebHostEnvironment environment,
    IQvdReportAccessService? qvdReportAccess = null)
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public async Task<QvdQueryResult> ExecuteAsync(string module, string queryPath, string originalSql, SqlCommand preparedCommand,
        CancellationToken cancellationToken = default)
    {
        if (qvdReportAccess is null || !await qvdReportAccess.CanReadAsync(module, cancellationToken))
            throw new UnauthorizedAccessException(QvdReportAccessService.AdminOnlyMessage);
        var options = configuration.GetSection("QvdReports").Get<QvdReportOptions>() ?? new();
        var parameters = preparedCommand.Parameters.Cast<SqlParameter>()
            .ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value, StringComparer.OrdinalIgnoreCase);
        var parts = queryPath.Replace('\\', '/').Split('/');
        if (parts.Length < 5 || parts[0] != "Sql" || !parts[1].Equals(module, StringComparison.OrdinalIgnoreCase) ||
            !parts[3].Equals("QVD", StringComparison.OrdinalIgnoreCase))
            throw QvdReportError.Create("Neteisingas QVD ataskaitos katalogas.", "Invalid QVD report folder.");
        var tables = QvdReportFiles.ReadSources(originalSql);
        foreach (var line in originalSql.Split('\n').Select(line => line.Trim()))
            if (line.StartsWith("-- QVD_COLUMN:", StringComparison.OrdinalIgnoreCase) &&
                !tables.Any(table => table.Alias.Equals(line[14..].Split('|')[0].Trim(), StringComparison.OrdinalIgnoreCase)))
                throw QvdReportError.Create("QVD_COLUMN naudoja nedeklaruota alias.", "QVD_COLUMN uses an undeclared alias.");
        var compiled = QvdSelectCompiler.Compile(preparedCommand.CommandText, tables.Select(table => table.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase), parameters);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 600)));
        var acquired = false;
        try
        {
            await Gate.WaitAsync(deadline.Token);
            acquired = true;
            return await Task.Run(() => Run(module, parts[2], originalSql, compiled, parameters, tables, options, deadline.Token), CancellationToken.None);
        }
        catch (DuckDBException) when (deadline.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw QvdReportError.Create("QVD uzklausa virsijo laiko riba. Sutrumpinkite laikotarpi.", "The QVD query exceeded its time limit. Use a shorter date range.");
        }
        catch (DuckDBException exception) when (exception.Message.Contains("Out of Memory", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("file system operations are disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw QvdReportError.Create("QVD uzklausai nepakanka leistinos atminties. Susiaurinkite filtrus.",
                "The QVD query exceeds the allowed memory. Narrow the filters.", exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw QvdReportError.Create("QVD uzklausa virsijo laiko riba. Sutrumpinkite laikotarpi.", "The QVD query exceeded its time limit. Use a shorter date range.");
        }
        finally { if (acquired) Gate.Release(); }
    }

    private QvdQueryResult Run(string module, string country, string sql, QvdCompiledSelect compiled,
        IReadOnlyDictionary<string, object?> parameters, IReadOnlyList<QvdTableSource> tables, QvdReportOptions options, CancellationToken ct)
    {
        var needsPreviewRoots = tables.Any(table => options.Sources.Any(pair =>
            pair.Key.Equals(table.Source, StringComparison.OrdinalIgnoreCase) && pair.Value.PreviewRootIndex.HasValue));
        var previewRoots = needsPreviewRoots
            ? new ApisQvdPreviewService(environment, Options.Create(
                configuration.GetSection(ApisOptions.SectionName).Get<ApisOptions>() ?? new())).ListRoots()
            : [];
        var inputs = tables.Select(table => (Table: table, Files: QvdReportFiles.Resolve(table, country, module,
            environment.ContentRootPath, options, parameters, ct, previewRoots))).ToArray();
        if (inputs.Sum(input => input.Files.Count) > Math.Clamp(options.MaxFiles, 1, 1000) ||
            inputs.SelectMany(input => input.Files).Sum(file => file.Length) > Math.Clamp(options.MaxScanBytes, 1, 16L * 1024 * 1024 * 1024))
            throw QvdReportError.Create("Virsytas QVD skaitomu failu kiekio arba dydzio limitas. Sutrumpinkite failu laikotarpi.",
                "The QVD file scan count or byte limit was exceeded. Use a shorter file date range.");
        using var connection = new DuckDBConnection("DataSource=:memory:;enable_external_access=false;autoload_known_extensions=false;" +
            "autoinstall_known_extensions=false;allow_unsigned_extensions=false;allow_community_extensions=false;" +
            "allow_persistent_secrets=false;threads=2;temp_directory=;max_temp_directory_size=0B;memory_limit=" +
            Math.Clamp(options.MemoryLimitMb, 64, 2048).ToString(CultureInfo.InvariantCulture) + "MB");
        connection.Open();
        ct.ThrowIfCancellationRequested();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE MACRO STRING_SPLIT(input, separator) AS TABLE SELECT unnest(string_to_array(input, separator)) AS value; SET lock_configuration=true;";
            setup.ExecuteNonQuery();
        }
        var matches = QvdReadPlanner.ReadMatches(sql, tables);
        var requiredColumns = compiled.RequiredColumns?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requiredColumns is not null)
            foreach (var match in matches.Values) requiredColumns.UnionWith(match.Columns.Concat(match.ParentColumns));
        var loader = new QvdBatchLoader(connection, options, parameters, compiled, matches, Append, ct);
        foreach (var input in inputs)
            loader.Load(input.Table, input.Files, QvdReportFiles.ReadTypes(sql, input.Table.Alias), requiredColumns);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM (" + compiled.Sql + ") AS qvd_result LIMIT " +
            (Math.Clamp(options.MaxResultRows, 1, 1_000_000) + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var name in compiled.Parameters)
            command.Parameters.Add(new DuckDBParameter(name, parameters["@" + name] ?? DBNull.Value));
        using var registration = ct.Register(command.Cancel);
        ct.ThrowIfCancellationRequested();
        try
        {
            using var reader = command.ExecuteReader();
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            if (columns.Length > Math.Clamp(options.MaxColumns, 1, 1000) || columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Length)
                throw QvdReportError.Create("Per daug rezultato stulpeliu arba pavadinimai kartojasi. Naudokite AS alias.", "Too many result columns or duplicate names. Use AS aliases.");
            var numeric = Enumerable.Range(0, reader.FieldCount).Where(index => IsNumeric(reader.GetFieldType(index)))
                .Select(reader.GetName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = new List<IReadOnlyDictionary<string, string?>>();
            long resultCharacters = 0;
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (rows.Count >= Math.Clamp(options.MaxResultRows, 1, 1_000_000))
                    throw QvdReportError.Create("QVD rezultatas per didelis. Susiaurinkite filtra arba naudokite grupavima.", "The QVD result is too large. Narrow the filter or use grouping.");
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < columns.Length; index++)
                {
                    var value = reader.IsDBNull(index) ? string.Empty : Convert.ToString(reader.GetValue(index), CultureInfo.CurrentCulture);
                    resultCharacters += value?.Length ?? 0;
                    if ((value?.Length ?? 0) > 32_000 || resultCharacters > 32_000_000)
                        throw QvdReportError.Create("QVD rezultato teksto dydis virsija riba.", "The QVD result text exceeds the size limit.");
                    row.Add(columns[index], value);
                }
                rows.Add(row);
            }
            return new(columns, rows, numeric, CultureInfo.CurrentCulture.Name,
                new QvdReadStatistics(inputs.Sum(input => input.Files.Count), loader.ScannedRows, loader.RetainedRows,
                    loader.RetainedBytes, new Dictionary<string, int>(loader.SourceRows, StringComparer.OrdinalIgnoreCase)));
        }
        catch (DuckDBException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (DuckDBException exception) when (exception.Message.Contains("Out of Memory", StringComparison.OrdinalIgnoreCase) ||
            (exception.Message.Contains("Cannot access directory", StringComparison.OrdinalIgnoreCase) &&
             exception.Message.Contains("file system operations are disabled", StringComparison.OrdinalIgnoreCase)))
        {
            throw QvdReportError.Create("QVD uzklausai nepakanka leistinos atminties. Sutrumpinkite laikotarpi arba pasirinkite maziau stulpeliu.",
                "The QVD query exceeds the allowed memory. Use a shorter date range or select fewer columns.", exception);
        }
    }

    private static bool IsNumeric(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or
        TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static void Append(IDuckDBAppenderRow row, Value value, string type, string field)
    {
        var text = value.ToString();
        if (text is null || (type != "VARCHAR" && string.IsNullOrWhiteSpace(text))) { row.AppendNullValue(); return; }
        try
        {
            double? number = value.Type switch
            {
                Bondski.QvdLib.ValueType.Int or Bondski.QvdLib.ValueType.DualInt => value.Int,
                Bondski.QvdLib.ValueType.Double or Bondski.QvdLib.ValueType.DualDouble => value.Double,
                _ => null
            };
            switch (type)
            {
                case "VARCHAR":
                    if (text.Length > 32_000) throw new FormatException();
                    row.AppendValue(text); break;
                case "BIGINT":
                    // Do not round a fractional value into a whole number.
                    var integer = number is { } n ? checked((decimal)n) : decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (decimal.Truncate(integer) != integer) throw new FormatException();
                    row.AppendValue(checked((long)integer)); break;
                case "DOUBLE":
                    var real = number ?? double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(real)) throw new FormatException();
                    row.AppendValue(real); break;
                case "DECIMAL(38,10)":
                    var decimalValue = number is { } numeric ? checked((decimal)numeric) : decimal.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (decimal.Round(decimalValue, 10) != decimalValue) throw new FormatException();
                    row.AppendValue(decimalValue); break;
                case "DATE": case "TIMESTAMP":
                    var date = number is { } serial ? DateTime.FromOADate(serial) : ParseDate(text);
                    if (type == "DATE") row.AppendValue(DateOnly.FromDateTime(date)); else row.AppendValue(date);
                    break;
                case "BOOLEAN":
                    row.AppendValue(text.ToLowerInvariant() switch { "1" or "true" => true, "0" or "false" => false, _ => throw new FormatException() }); break;
                default: throw new FormatException();
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException or InvalidValueException)
        {
            throw QvdReportError.Create($"QVD lauko '{field}' reiksme neatitinka tipo {type}.", $"A value in QVD field '{field}' does not match type {type}.", exception);
        }
    }

    private static DateTime ParseDate(string value)
    {
        if (DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyyMMdd", "dd/MM/yyyy", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "O"],
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) return date;
        throw new FormatException();
    }
}
