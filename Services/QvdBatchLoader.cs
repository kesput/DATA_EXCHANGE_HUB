using Bondski.QvdLib;
using DuckDB.NET.Data;

namespace DATA_EXCHANGE_HUB.Services;

// QVDs are scanned one at a time. Only bounded batches and matching rows enter DuckDB.
internal sealed class QvdBatchLoader(DuckDBConnection connection, QvdReportOptions options,
    IReadOnlyDictionary<string, object?> parameters, QvdCompiledSelect compiled, IReadOnlyDictionary<string, QvdMatch> matches,
    Action<IDuckDBAppenderRow, Value, string, string> append, CancellationToken ct)
{
    private const int BatchSize = 2048;
    private readonly Dictionary<string, Dictionary<string, string>> schemas = new(StringComparer.OrdinalIgnoreCase);
    public long ScannedRows { get; private set; }
    public int RetainedRows { get; private set; }
    public long RetainedBytes { get; private set; }
    public Dictionary<string, int> SourceRows { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Load(QvdTableSource table, IReadOnlyList<QvdInputFile> files, Dictionary<string, string> types, IReadOnlySet<string>? requiredColumns)
    {
        var plan = compiled.ReadPlan?.Source.Equals(table.Alias, StringComparison.OrdinalIgnoreCase) == true ? compiled.ReadPlan : null;
        matches.TryGetValue(table.Alias, out var match);
        string[]? fields = null;
        HashSet<string>? schema = null;
        Dictionary<string, string>? columnTypes = null;
        var stage = "__qvd_stage_" + Guid.NewGuid().ToString("N");
        var alias = plan?.Alias ?? "qvd_row";
        string? where = null;
        string? statsSql = null;
        string? insertSql = null;
        HashSet<int>? keyHashes = null;
        SourceRows[table.Alias] = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            using var reader = new QvdReader(file.Path);
            var names = reader.Header.Fields.Select(field => field.Name).ToArray();
            var actualNames = names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            if (names.Length == 0 || names.Length > Math.Clamp(options.MaxColumns, 1, 1000) ||
                actualNames.Count != names.Length || names.Any(name => string.IsNullOrWhiteSpace(name) ||
                    name.Equals("_QVD_FILE", StringComparison.OrdinalIgnoreCase) || name.Equals("_QVD_DATE", StringComparison.OrdinalIgnoreCase)))
                throw QvdReportError.Create("Netinkama QVD stulpeliu schema.", "Invalid QVD column schema.");
            if (ScannedRows + reader.Header.NoOfRecords > Math.Clamp(options.MaxScannedRows, 1, 100_000_000))
                throw QvdReportError.Create("Virsytas skaitomu QVD eiluciu limitas. Sutrumpinkite failu laikotarpi.", "The QVD scan row limit was exceeded. Use a shorter file date range.");
            if (fields is null)
            {
                schema = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
                fields = requiredColumns is null ? names : names.Where(requiredColumns.Contains).ToArray();
                if (types.Keys.Any(name => !schema.Contains(name)))
                    throw QvdReportError.Create("QVD_COLUMN laukas nerastas QVD faile.", "A QVD_COLUMN field was not found in the QVD file.");
                columnTypes = fields.ToDictionary(field => field, field => types.GetValueOrDefault(field, "VARCHAR"), StringComparer.OrdinalIgnoreCase);
                columnTypes.Add("_QVD_FILE", "VARCHAR");
                columnTypes.Add("_QVD_DATE", "DATE");
                schemas.Add(table.Alias, columnTypes);
                var definition = string.Join(",", columnTypes.Select(column => Q(column.Key) + " " + column.Value));
                Execute("CREATE TABLE " + Q(table.Alias) + " (" + definition + "); CREATE TABLE " + Q(stage) + " (" + definition + ");");
                var conditions = new List<string>();
                if (plan is not null) conditions.Add("(" + plan.Predicate + ")");
                if (match is not null)
                {
                    if (!schemas.TryGetValue(match.Parent, out var parentSchema) || match.Columns.Any(column => !columnTypes.ContainsKey(column)) ||
                        match.ParentColumns.Any(column => !parentSchema.ContainsKey(column)))
                        throw QvdReportError.Create("QVD_MATCH raktas nerastas saltiniuose.", "A QVD_MATCH key was not found in its sources.");
                    var parentAlias = alias.Equals("__qvd_parent", StringComparison.OrdinalIgnoreCase) ? "__qvd_parent_1" : "__qvd_parent";
                    conditions.Add("EXISTS (SELECT 1 FROM " + Q(match.Parent) + " AS " + Q(parentAlias) + " WHERE " +
                        string.Join(" AND ", match.Columns.Select((column, index) => Q(alias) + "." + Q(column) + " = " + Q(parentAlias) + "." + Q(match.ParentColumns[index]))) + ")");
                    keyHashes = ParentHashes(match, columnTypes, parentSchema);
                }
                where = conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);
                var from = " FROM " + Q(stage) + " AS " + Q(alias) + where;
                var size = "64 + " + string.Join(" + ", columnTypes.Select(column => column.Value == "VARCHAR"
                    ? "32 + COALESCE(OCTET_LENGTH(ENCODE(" + Q(alias) + "." + Q(column.Key) + ")), 0)" : "16"));
                statsSql = "SELECT COUNT(*), CAST(COALESCE(SUM(" + size + "), 0) AS BIGINT)" + from;
                insertSql = "INSERT INTO " + Q(table.Alias) + " SELECT " +
                    string.Join(",", columnTypes.Keys.Select(column => Q(alias) + "." + Q(column))) + from;
            }
            else if (!schema!.SetEquals(names))
                throw QvdReportError.Create("Pasirinktu dienu QVD stulpeliu schemos nesutampa.", "The selected daily QVD files have different column schemas.");

            var equalities = plan?.Equalities.Where(equal => actualNames.ContainsKey(equal.Column) && types.GetValueOrDefault(equal.Column, "VARCHAR") == "VARCHAR").ToArray() ?? [];
            var fileRows = 0;
            var finished = false;
            while (!finished)
            {
                var batchRows = 0;
                long batchBytes = 0;
                using (var appender = connection.CreateAppender(stage))
                {
                    while (batchRows < BatchSize && batchBytes < 1024 * 1024)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!reader.NextRow()) { finished = true; break; }
                        fileRows++;
                        ScannedRows++;
                        if (ScannedRows > Math.Clamp(options.MaxScannedRows, 1, 100_000_000))
                            throw QvdReportError.Create("Virsytas skaitomu QVD eiluciu limitas.", "The QVD scan row limit was exceeded.");
                        if (equalities.Any(equal => !string.Equals(reader[actualNames[equal.Column]].ToString(), equal.Value, StringComparison.Ordinal))) continue;
                        if (keyHashes is not null && match is not null && !MatchesHash(reader, file, actualNames, match, keyHashes)) continue;
                        var row = appender.CreateRow();
                        foreach (var field in fields)
                        {
                            var value = reader[actualNames[field]];
                            batchBytes += 32L + (value.ToString()?.Length ?? 0) * 2L;
                            append(row, value, types.GetValueOrDefault(field, "VARCHAR"), field);
                        }
                        row.AppendValue(file.RelativePath).AppendValue(file.Date).EndRow();
                        batchRows++;
                    }
                    appender.Close();
                }
                if (batchRows > 0) Transfer(table.Alias, stage, statsSql!, insertSql!, plan);
            }
            var current = new FileInfo(file.Path);
            if (fileRows != reader.Header.NoOfRecords || current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteTimeUtc)
                throw QvdReportError.Create("QVD failas pakito arba yra nepilnas. Pakartokite uzklausa baigus failo formavima.", "The QVD file changed or is incomplete. Retry after file generation finishes.");
        }
        Execute("DROP TABLE " + Q(stage));
    }

    private void Transfer(string source, string stage, string statsSql, string insertSql, QvdSourceReadPlan? plan)
    {
        ct.ThrowIfCancellationRequested();
        using var stats = connection.CreateCommand();
        stats.CommandText = statsSql;
        Bind(stats, plan);
        long count, bytes;
        using (var cancel = ct.Register(stats.Cancel))
        using (var result = stats.ExecuteReader())
        {
            result.Read();
            count = Convert.ToInt64(result.GetValue(0));
            bytes = Convert.ToInt64(result.GetValue(1));
        }
        if (RetainedRows + count > Math.Clamp(options.MaxInputRows, 1, 10_000_000))
            throw QvdReportError.Create("Po QVD filtru liko per daug eiluciu. Susiaurinkite filtrus.", "Too many QVD rows remain after filtering. Narrow the filters.");
        if (RetainedBytes + bytes > Math.Clamp(options.MaxTotalBytes, 1, 2L * 1024 * 1024 * 1024))
            throw QvdReportError.Create("Po QVD filtru duomenu dydis virsija atminties riba. Susiaurinkite filtrus.", "The filtered QVD data exceeds its memory budget. Narrow the filters.");
        if (count > 0)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = insertSql;
            Bind(insert, plan);
            using var cancel = ct.Register(insert.Cancel);
            insert.ExecuteNonQuery();
            RetainedRows += checked((int)count);
            RetainedBytes += bytes;
            SourceRows[source] += checked((int)count);
        }
        Execute("DELETE FROM " + Q(stage));
    }

    private void Bind(DuckDBCommand command, QvdSourceReadPlan? plan)
    {
        if (plan is null) return;
        foreach (var name in plan.Parameters) command.Parameters.Add(new DuckDBParameter(name, parameters["@" + name] ?? DBNull.Value));
    }

    private HashSet<int>? ParentHashes(QvdMatch match, Dictionary<string, string> child, Dictionary<string, string> parent)
    {
        // Hashes only reject impossible keys; exact SQL EXISTS is still applied to every candidate.
        for (var index = 0; index < match.Columns.Length; index++)
        {
            var type = child[match.Columns[index]];
            if (type != parent[match.ParentColumns[index]] || type != "VARCHAR" &&
                !(type == "DATE" && match.Columns[index].Equals("_QVD_DATE", StringComparison.OrdinalIgnoreCase))) return null;
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT " + string.Join(",", match.ParentColumns.Select(Q)) + " FROM " + Q(match.Parent) +
            " WHERE " + string.Join(" AND ", match.ParentColumns.Select(column => Q(column) + " IS NOT NULL")) + " LIMIT 100001";
        using var cancel = ct.Register(command.Cancel);
        using var reader = command.ExecuteReader();
        var hashes = new HashSet<int>();
        var count = 0;
        while (reader.Read())
        {
            if (++count > 100000) return null;
            var hash = new HashCode();
            for (var index = 0; index < match.ParentColumns.Length; index++)
            {
                var value = reader.GetValue(index);
                if (parent[match.ParentColumns[index]] == "VARCHAR") hash.Add((string)value, StringComparer.Ordinal);
                else hash.Add(value is DateOnly date ? date.DayNumber : DateOnly.FromDateTime((DateTime)value).DayNumber);
            }
            hashes.Add(hash.ToHashCode());
        }
        return hashes;
    }

    private static bool MatchesHash(QvdReader reader, QvdInputFile file, Dictionary<string, string> names, QvdMatch match, HashSet<int> hashes)
    {
        if (hashes.Count == 0) return false;
        var hash = new HashCode();
        foreach (var column in match.Columns)
        {
            if (column.Equals("_QVD_DATE", StringComparison.OrdinalIgnoreCase))
            {
                if (file.Date is not { } date) return false;
                hash.Add(date.DayNumber);
            }
            else
            {
                var text = column.Equals("_QVD_FILE", StringComparison.OrdinalIgnoreCase) ? file.RelativePath : reader[names[column]].ToString();
                if (text is null) return false;
                hash.Add(text, StringComparer.Ordinal);
            }
        }
        return hashes.Contains(hash.ToHashCode());
    }

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static string Q(string value) => QvdSelectCompiler.Quote(value);
}
