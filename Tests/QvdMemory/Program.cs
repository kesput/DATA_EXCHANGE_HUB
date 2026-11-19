using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bondski.QvdLib;
using DuckDB.NET.Data;

// Isolated capacity benchmark. Does not modify application limits, QVDs, or databases.
// Only field names and length histograms are saved; source values are not saved.
if (args.Length == 3 && args[0] == "profile")
{
    using var qvd = new QvdReader(args[1]);
    var fields = qvd.Header.Fields.Select(field => new FieldProfile { Name = field.Name }).ToArray();
    var rowCount = 0;
    while (qvd.NextRow())
    {
        rowCount++;
        foreach (var field in fields)
        {
            var length = qvd[field.Name].ToString()?.Length ?? 0;
            field.TotalCharacters += length;
            field.MaxLength = Math.Max(field.MaxLength, length);
            field.Lengths[length] = field.Lengths.GetValueOrDefault(length) + 1;
        }
    }
    var profile = new Profile(rowCount, fields.OrderByDescending(field => field.TotalCharacters).Take(15).ToArray());
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    File.WriteAllText(args[2], JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PROFILE source_rows={rowCount}; selected_fields={profile.Fields.Length}");
    foreach (var field in profile.Fields)
        Console.WriteLine($"FIELD {field.Name}; mean_chars={(double)field.TotalCharacters / rowCount:F2}; max_chars={field.MaxLength}");
    Console.WriteLine($"ROW mean_chars={profile.Fields.Sum(field => (double)field.TotalCharacters / rowCount):F2}; max_chars={profile.Fields.Sum(field => field.MaxLength)}");
    return;
}
if (args.Length != 6 || args[0] != "run")
    throw new ArgumentException("profile <qvd> <profile.json> OR run <profile.json> <result|pipeline> <rows> <observed|max> <output.json>");
var input = JsonSerializer.Deserialize<Profile>(File.ReadAllText(args[1]))!;
var mode = args[2];
var target = int.Parse(args[3], CultureInfo.InvariantCulture);
var distribution = args[4];
if (mode is not ("result" or "pipeline") || distribution is not ("observed" or "max") || target is < 1 or > 1_000_000)
    throw new ArgumentException("Unsupported benchmark case.");
var lengths = input.Fields.Select(field => field.Lengths.OrderBy(pair => pair.Key)
    .SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value)).ToArray()).ToArray();
int Length(int row, int column) => distribution == "max" ? input.Fields[column].MaxLength :
    lengths[column][(int)(((long)row * 104729 + column * 9973L) % input.Rows)];
string Value(int row, int column) => string.Create(Length(row, column), (row, column), (span, state) =>
{
    span.Fill((char)('A' + state.column % 26));
    var value = (ulong)state.row * 16 + (uint)state.column;
    for (var index = 0; index < span.Length && value != 0; index++, value /= 36)
        span[index] = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[(int)(value % 36)];
});
var process = Process.GetCurrentProcess();
var rows = new List<IReadOnlyDictionary<string, string?>>();
var samples = new List<object>();
var timer = Stopwatch.StartNew();
long peakPrivate = 0, peakWorkingSet = 0, characters = 0;
var textLimitRow = 0;
var inputRows = 0;
object Snapshot(string stage, bool collect = false)
{
    var managed = GC.GetTotalMemory(collect);
    process.Refresh();
    peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
    peakWorkingSet = Math.Max(peakWorkingSet, process.PeakWorkingSet64);
    return new { stage, seconds = timer.Elapsed.TotalSeconds, inputRows, resultRows = rows.Count, managed,
        privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64 };
}
void Guard()
{
    _ = Snapshot("sample");
    var memory = new NativeMemoryStatus { Length = (uint)Marshal.SizeOf<NativeMemoryStatus>() };
    if (!NativeMethods.GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("Cannot read host memory status.");
    if (memory.AvailablePhysical < 512UL * 1024 * 1024 || memory.AvailablePageFile < 512UL * 1024 * 1024 ||
        process.PrivateMemorySize64 > 2304L * 1024 * 1024)
        throw new InvalidOperationException("BENCHMARK_MEMORY_RESERVE: stopped to retain 512 MiB host headroom or the 2304 MiB process cap.");
}
void AddRow(Func<int, string> getValue)
{
    var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var column = 0; column < input.Fields.Length; column++)
    {
        var value = getValue(column);
        characters += value.Length;
        if (textLimitRow == 0 && characters > 32_000_000) textLimitRow = rows.Count + 1;
        row.Add(input.Fields[column].Name, value);
    }
    rows.Add(row);
}
samples.Add(Snapshot("baseline", true));
var status = "completed";
string? failure = null;
try
{
    Guard();
    if (mode == "result")
    {
        for (var row = 0; row < target; row++)
        {
            var index = row;
            AddRow(column => Value(index, column));
            if (rows.Count % 10000 == 0) Guard();
        }
    }
    else
    {
        using var connection = new DuckDBConnection("DataSource=:memory:;enable_external_access=false;autoload_known_extensions=false;" +
            "autoinstall_known_extensions=false;allow_unsigned_extensions=false;allow_community_extensions=false;" +
            "allow_persistent_secrets=false;threads=2;temp_directory=;max_temp_directory_size=0B;memory_limit=256MB");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE source_data (" + string.Join(",", input.Fields.Select((_, i) => $"c{i} VARCHAR")) + ");";
            create.ExecuteNonQuery();
        }
        using (var appender = connection.CreateAppender("source_data"))
        {
            for (var row = 0; row < target; row++)
            {
                var append = appender.CreateRow();
                for (var column = 0; column < input.Fields.Length; column++) append.AppendValue(Value(row, column));
                append.EndRow();
                inputRows++;
                if (inputRows % 10000 == 0) Guard();
            }
            appender.Close();
        }
        samples.Add(Snapshot("input_loaded", true));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM source_data";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            AddRow(column => reader.IsDBNull(column) ? string.Empty : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)!);
            if (rows.Count % 10000 == 0) Guard();
        }
        samples.Add(Snapshot("query_and_result", true));
    }
    samples.Add(Snapshot("result_retained", true));
}
catch (Exception exception) when (exception is InvalidOperationException or DuckDBException or OutOfMemoryException)
{
    status = "stopped";
    failure = exception.Message;
    samples.Add(Snapshot("stopped", true));
}
var report = new { mode, distribution, targetRows = target, inputRows, resultRows = rows.Count, status, failure,
    characters, textLimitRow, peakPrivate, peakWorkingSet, elapsedSeconds = timer.Elapsed.TotalSeconds, samples };
File.WriteAllText(args[5], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
GC.KeepAlive(rows);

internal sealed class FieldProfile
{
    public string Name { get; set; } = "";
    public long TotalCharacters { get; set; }
    public int MaxLength { get; set; }
    public Dictionary<int, int> Lengths { get; set; } = [];
}
internal sealed record Profile(int Rows, FieldProfile[] Fields);
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMemoryStatus
{
    internal uint Length, MemoryLoad;
    internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
}
internal static class NativeMethods
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref NativeMemoryStatus status);
}
