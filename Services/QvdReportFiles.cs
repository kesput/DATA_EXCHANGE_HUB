using System.Globalization;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

internal sealed record QvdTableSource(string Alias, string Source, string Pattern, string? From, string? To);
internal sealed record QvdInputFile(string Path, string RelativePath, DateOnly? Date, long Length, DateTime LastWriteTimeUtc);

internal static partial class QvdReportFiles
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    internal static partial Regex IdentifierPattern();
    [GeneratedRegex(@"\{date:([yMd._-]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    public static IReadOnlyList<QvdTableSource> ReadSources(string sql)
    {
        var sources = new List<QvdTableSource>();
        foreach (var line in sql.Split('\n').Select(line => line.Trim()))
        {
            if (!line.StartsWith("-- QVD_SOURCE:", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line[14..].Trim().Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length is not (3 or 5) || !IdentifierPattern().IsMatch(parts[0]) ||
                !IdentifierPattern().IsMatch(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
                throw QvdReportError.Create("Neteisingas QVD_SOURCE: alias|saltinis|kelias[|@NUO|@IKI].", "Invalid QVD_SOURCE: alias|source|path[|@FROM|@TO].");
            if (sources.Any(source => source.Alias.Equals(parts[0], StringComparison.OrdinalIgnoreCase)))
                throw QvdReportError.Create("Pasikartojantis QVD lenteles alias.", "Duplicate QVD table alias.");
            sources.Add(new(parts[0], parts[1], parts[2], parts.Length == 5 ? parts[3] : null, parts.Length == 5 ? parts[4] : null));
        }
        if (sources.Count is < 1 or > 8)
            throw QvdReportError.Create("QVD uzklausai reikia nuo 1 iki 8 QVD_SOURCE eiluciu.", "A QVD query needs 1 to 8 QVD_SOURCE declarations.");
        return sources;
    }

    public static Dictionary<string, string> ReadTypes(string sql, string alias)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in sql.Split('\n').Select(line => line.Trim()))
        {
            if (!line.StartsWith("-- QVD_COLUMN:", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line[14..].Trim().Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1]))
                throw QvdReportError.Create("Neteisingas QVD_COLUMN: alias|stulpelis|tipas.", "Invalid QVD_COLUMN: alias|column|type.");
            if (!parts[0].Equals(alias, StringComparison.OrdinalIgnoreCase)) continue;
            if (!result.TryAdd(parts[1], NormalizeType(parts[2])))
                throw QvdReportError.Create("Pasikartojantis QVD_COLUMN.", "Duplicate QVD_COLUMN.");
        }
        return result;
    }

    public static string NormalizeType(string value) => value.ToLowerInvariant() switch
    {
        "text" => "VARCHAR",
        "int" => "BIGINT",
        "number" => "DOUBLE",
        "decimal" => "DECIMAL(38,10)",
        "date" => "DATE",
        "datetime" => "TIMESTAMP",
        "bit" => "BOOLEAN",
        _ => throw QvdReportError.Create($"Nepalaikomas QVD tipas: {value}.", $"Unsupported QVD type: {value}.")
    };

    public static IReadOnlyList<QvdInputFile> Resolve(QvdTableSource table, string country, string module,
        string contentRoot, QvdReportOptions options, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct,
        IReadOnlyList<ApisQvdRoot> previewRoots)
    {
        var source = options.Sources.FirstOrDefault(pair => pair.Key.Equals(table.Source, StringComparison.OrdinalIgnoreCase)).Value;
        if (source is null ||
            !source.Countries.Contains(country, StringComparer.OrdinalIgnoreCase) ||
            !source.Modules.Contains(module, StringComparer.OrdinalIgnoreCase))
            throw QvdReportError.Create($"QVD saltinis '{table.Source}' nesukonfiguruotas arba neleidziamas siai saliai / moduliui.",
                $"QVD source '{table.Source}' is not configured or not allowed for this country / module.");
        var rootPath = source.RootPath;
        if (source.PreviewRootIndex is int rootIndex)
        {
            if (!string.IsNullOrWhiteSpace(rootPath))
                throw QvdReportError.Create("QVD saltiniui nurodykite tik RootPath arba PreviewRootIndex.",
                    "Configure either RootPath or PreviewRootIndex for a QVD source, not both.");
            if (rootIndex < 0 || rootIndex >= previewRoots.Count)
                throw QvdReportError.Create("QVD saltinio ARGUS perziuros katalogas nesukonfiguruotas. Patikrinkite PreviewRootIndex ir Apis:QvdRoots.",
                    "The ARGUS QVD preview root is not configured. Check PreviewRootIndex and Apis:QvdRoots.");
            rootPath = previewRoots[rootIndex].Path;
        }
        if (string.IsNullOrWhiteSpace(rootPath))
            throw QvdReportError.Create("QVD saltiniui reikia RootPath arba PreviewRootIndex.",
                "A QVD source requires RootPath or PreviewRootIndex.");
        var root = Path.GetFullPath(Path.IsPathRooted(rootPath) ? rootPath : Path.Combine(contentRoot, rootPath));
        if (!Directory.Exists(root)) throw QvdReportError.Create("QVD saltinio katalogas nepasiekiamas. Patikrinkite serverio skaitymo teises ir UNC kelia.",
            "The QVD source folder is unavailable. Check server read permissions and the UNC path.");
        CheckLinks(root, root);
        var pattern = table.Pattern.Replace('\\', '/');
        var pathShape = DatePattern().Replace(pattern, "DATE");
        if (Path.IsPathRooted(pathShape) || pathShape.Contains(':') || pathShape.Split('/').Any(part => part is ".." or "." or ""))
            throw QvdReportError.Create("QVD kelias turi buti santykinis, be '..'.", "The QVD path must be relative and cannot contain '..'.");
        var dates = new List<DateOnly?> { null };
        if (DatePattern().IsMatch(pattern))
        {
            if (table.From is null || table.To is null || !DatePattern().Matches(pattern).Any(match => match.Groups[1].Value.Contains('d')))
                throw QvdReportError.Create("Dieniniam QVD keliui reikia datos sablono ir @NUO / @IKI parametru.", "A daily QVD path needs a date pattern and @FROM / @TO parameters.");
            var from = ReadDate(parameters, table.From);
            var to = ReadDate(parameters, table.To);
            if (from > to || to.DayNumber - from.DayNumber + 1 > Math.Clamp(options.MaxDays, 1, 366))
                throw QvdReportError.Create("Netinkamas arba per ilgas QVD laikotarpis.", "The QVD date range is invalid or too long.");
            dates.Clear();
            for (var day = from.DayNumber; day <= to.DayNumber; day++) dates.Add(DateOnly.FromDayNumber(day));
        }
        else if (table.From is not null || table.To is not null)
            throw QvdReportError.Create("QVD_SOURCE datos parametrai reikalauja {date:yyyyMMdd} kelio sablono.", "QVD_SOURCE date parameters require a {date:yyyyMMdd} path pattern.");
        var files = new Dictionary<string, QvdInputFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var date in dates)
        {
            ct.ThrowIfCancellationRequested();
            var relative = date is null ? pattern : DatePattern().Replace(pattern, match => date.Value.ToString(match.Groups[1].Value, CultureInfo.InvariantCulture));
            if (relative.Contains('{') || relative.Contains('}') || !relative.EndsWith(".qvd", StringComparison.OrdinalIgnoreCase))
                throw QvdReportError.Create("Neteisingas QVD failo sablonas.", "Invalid QVD file pattern.");
            var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw QvdReportError.Create("QVD failas yra uz leidziamo katalogo.", "The QVD file is outside the allowed folder.");
            var directory = Path.GetDirectoryName(candidate)!;
            if (directory.Contains('*') || directory.Contains('?'))
                throw QvdReportError.Create("QVD * ir ? leidziami tik failo pavadinime.", "QVD * and ? are only allowed in the filename.");
            CheckLinks(directory, root);
            var matches = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, Path.GetFileName(candidate), SearchOption.TopDirectoryOnly)
                    .Take(Math.Clamp(options.MaxFiles, 1, 1000) + 1).ToArray()
                : [];
            if (matches.Length == 0)
                throw QvdReportError.Create($"QVD failas nerastas: {relative}. Rezultatas nepateiktas, kad netruktu dienos duomenu.",
                    $"QVD file not found: {relative}. No partial result was returned.");
            foreach (var path in matches)
            {
                CheckLinks(path, root);
                var info = new FileInfo(path);
                if (info.Length > Math.Clamp(options.MaxFileBytes, 1, 1024L * 1024 * 1024))
                    throw QvdReportError.Create("QVD failas virsija nustatyta dydzio riba.", "The QVD file exceeds the configured size limit.");
                files.TryAdd(path, new(path, Path.GetRelativePath(root, path).Replace('\\', '/'), date, info.Length, info.LastWriteTimeUtc));
                if (files.Count > Math.Clamp(options.MaxFiles, 1, 1000))
                    throw QvdReportError.Create("QVD failu per daug. Sutrumpinkite laikotarpi.", "Too many QVD files. Use a shorter date range.");
            }
        }
        return files.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static DateOnly ReadDate(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!name.StartsWith('@') || !parameters.TryGetValue(name, out var value) || value is null or DBNull)
            throw QvdReportError.Create($"Truksta QVD datos parametro {name}.", $"Missing QVD date parameter {name}.");
        if (value is DateTime date) return DateOnly.FromDateTime(date);
        if (value is DateOnly dateOnly) return dateOnly;
        if (DateOnly.TryParseExact(Convert.ToString(value, CultureInfo.InvariantCulture), ["yyyy-MM-dd", "yyyyMMdd", "dd/MM/yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed;
        throw QvdReportError.Create($"Neteisinga QVD data {name}.", $"Invalid QVD date {name}.");
    }

    internal static void CheckLinks(string path, string root)
    {
        var current = Path.GetFullPath(path);
        var limit = Path.GetFullPath(root);
        while (true)
        {
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw QvdReportError.Create("QVD simbolines nuorodos ir junction katalogai neleidziami.", "QVD symbolic links and junction folders are not allowed.");
            if (current.Equals(limit, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current) ?? throw QvdReportError.Create("Neteisingas QVD kelias.", "Invalid QVD path.");
        }
    }
}
