using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ApisQvdPreviewService(
    IWebHostEnvironment environment,
    IOptions<ApisOptions> options) : IApisQvdPreviewService
{
    private const string QvdRoot = "Data/Apis/Qvd";
    private const string QvdAssemblyName = "Bondski.QvdLib";
    private readonly ApisOptions _options = options.Value;

    public IReadOnlyList<ApisQvdRoot> ListRoots()
    {
        return GetRoots()
            .Select(root => new ApisQvdRoot(root.Path, Directory.Exists(root.Path)))
            .ToArray();
    }
    public IReadOnlyList<ApisQvdFile> ListFiles()
    {
        return GetRoots()
            .SelectMany(root => Directory.Exists(root.Path)
                ? Directory.EnumerateFiles(root.Path, "*.qvd", SearchOption.AllDirectories).Select(path => BuildFile(root, path))
                : [])
            .OrderByDescending(file => file.LastModified)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ApisQvdPreview? ReadPreview(string? fileId, int maxRows)
    {
        var files = ListFiles();
        var selectedFile = string.IsNullOrWhiteSpace(fileId)
            ? files.FirstOrDefault()
            : files.FirstOrDefault(file => file.Id.Equals(fileId, StringComparison.OrdinalIgnoreCase));

        if (selectedFile is null)
        {
            return null;
        }

        var path = ResolveFilePath(selectedFile.Id);
        var header = ReadHeader(path);
        var warning = header.Warning;
        var rows = Array.Empty<IReadOnlyDictionary<string, string?>>();

        try
        {
            rows = ReadRows(path, header.Fields.Select(field => field.Name).ToArray(), Math.Clamp(maxRows, 1, 1000));
        }
        catch (Exception exception)
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? $"QVD eiluciu preview nepavyko nuskaityti: {exception.Message}"
                : $"{warning} QVD eiluciu preview nepavyko nuskaityti: {exception.Message}";
        }

        selectedFile = selectedFile with


        {


            CreatorDoc = header.CreatorDoc,


            CreateUtcTime = header.CreateUtcTime


        };



        return new ApisQvdPreview(


            selectedFile,
            header.TotalRecords,
            header.Fields,
            rows,
            warning);
    }

    private ApisQvdFile BuildFile(QvdRootDefinition root, string path)
    {
        var info = new FileInfo(path);
        var relativePath = Path.GetRelativePath(root.Path, path).Replace('\\', '/');
        var displayRoot = root.IsBuiltIn
            ? QvdRoot
            : root.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return new ApisQvdFile(
            $"{root.Index}|{relativePath}",
            Path.GetFileName(path),
            $"{displayRoot.Replace('\\', '/')}/{relativePath}",
            info.Length,
            info.LastWriteTime,
            null,
            null);
    }

    private string ResolveFilePath(string fileId)
    {
        var separatorIndex = fileId.IndexOf('|', StringComparison.Ordinal);
        if (separatorIndex <= 0 || !int.TryParse(fileId[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rootIndex))
        {
            throw new InvalidOperationException("Netinkamas QVD failo identifikatorius.");
        }

        var root = GetRoots().FirstOrDefault(item => item.Index == rootIndex)
            ?? throw new InvalidOperationException("QVD root aplankas nerastas.");
        var relativePath = fileId[(separatorIndex + 1)..];
        var fullPath = Path.GetFullPath(Path.Combine(root.Path, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(root.Path);

        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".qvd", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Netinkamas QVD failo kelias.");
        }

        return fullPath;
    }

    private IReadOnlyList<QvdRootDefinition> GetRoots()
    {
        var roots = new List<QvdRootDefinition>
        {
            new(0, Path.GetFullPath(Path.Combine(environment.ContentRootPath, QvdRoot)), true)
        };

        roots.AddRange(_options.QvdRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select((root, index) => new QvdRootDefinition(index + 1, Path.GetFullPath(root), false)));

        return roots;
    }

    private static QvdHeader ReadHeader(string path)
    {
        var xml = ReadHeaderXml(path);
        var document = XDocument.Parse(xml);
        var root = document.Root ?? throw new InvalidOperationException("QVD headeris tuscias.");
        var fields = root
            .Descendants()
            .Where(element => element.Name.LocalName.Equals("QvdFieldHeader", StringComparison.OrdinalIgnoreCase))
            .Select(element => new ApisQvdField(
                ReadChildValue(element, "FieldName") ?? "(be pavadinimo)",
                ReadChildValue(element, "NumberFormat"),
                TryParseInt(ReadChildValue(element, "NoOfSymbols"))))
            .ToArray();
        var totalRecords = TryParseInt(ReadChildValue(root, "NoOfRecords")) ?? 0;
        var creatorDoc = ReadChildValue(root, "CreatorDoc");
        var createUtcTime = TryParseDateTime(ReadChildValue(root, "CreateUtcTime"));
        var warning = fields.Length == 0
            ? "QVD headeris perskaitytas, bet lauku sarasas nerastas."
            : null;

        return new QvdHeader(totalRecords, fields, warning, creatorDoc, createUtcTime);
    }

    private static string ReadHeaderXml(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: false);
        var buffer = new char[4096];
        var xml = string.Empty;

        while (!reader.EndOfStream)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            xml += new string(buffer, 0, read);
            var endIndex = xml.IndexOf("</QvdTableHeader>", StringComparison.OrdinalIgnoreCase);
            if (endIndex >= 0)
            {
                return xml[..(endIndex + "</QvdTableHeader>".Length)];
            }

            if (xml.Length > 10_000_000)
            {
                throw new InvalidOperationException("QVD headeris didesnis nei tiketasi.");
            }
        }

        throw new InvalidOperationException("QVD headerio pabaiga nerasta.");
    }

    private static IReadOnlyDictionary<string, string?>[] ReadRows(
        string path,
        IReadOnlyList<string> fields,
        int maxRows)
    {
        if (fields.Count == 0)
        {
            return [];
        }

        var readerType = Assembly
            .Load(QvdAssemblyName)
            .GetTypes()
            .FirstOrDefault(type => type.Name.Equals("QvdReader", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("QvdReader klase nerasta Bondski.QvdLib bibliotekoje.");
        using var reader = Activator.CreateInstance(readerType, path) as IDisposable;
        if (reader is null)
        {
            throw new InvalidOperationException("Nepavyko sukurti QvdReader objekto.");
        }

        var nextRow = readerType.GetMethod("NextRow", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("QvdReader.NextRow metodas nerastas.");
        var indexer = readerType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, null, [typeof(string)], null)
            ?? throw new InvalidOperationException("QvdReader indeksatorius pagal stulpelio varda nerastas.");
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (rows.Count < maxRows && nextRow.Invoke(reader, null) is true)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                var rawValue = indexer.GetValue(reader, [field]);
                values[field] = Convert.ToString(rawValue, CultureInfo.CurrentCulture);
            }

            rows.Add(values);
        }

        return rows.ToArray();
    }

    private static string? ReadChildValue(XElement element, string childName)
    {
        return element
            .Elements()
            .FirstOrDefault(child => child.Name.LocalName.Equals(childName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private static DateTime? TryParseDateTime(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private sealed record QvdRootDefinition(int Index, string Path, bool IsBuiltIn);

    private sealed record QvdHeader(
        int TotalRecords,
        IReadOnlyList<ApisQvdField> Fields,
        string? Warning,
        string? CreatorDoc,
        DateTime? CreateUtcTime);
}