using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ExcelWorkbookService : IExcelWorkbookService
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

    private static readonly XNamespace SpreadsheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace OfficeRelationshipNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly CultureInfo LithuanianCulture = CultureInfo.GetCultureInfo("lt-LT");

    public byte[] CreateWorkbook(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string worksheetName,
        bool gridlines = false,
        IReadOnlyDictionary<string, string>? numberFormats = null)
    {
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("Eksportui nerasta stulpeliu.");
        }

        var columnFormats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (numberFormats is not null)
        {
            foreach (var pair in numberFormats)
                if (headers.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) columnFormats[pair.Key] = pair.Value;
        }
        var formats = columnFormats.Values.Distinct(StringComparer.Ordinal).ToList();
        var includeStyles = gridlines || formats.Count > 0;
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXml(archive, "[Content_Types].xml", CreateContentTypesDocument(includeStyles));
            WriteXml(archive, "_rels/.rels", CreateRootRelationshipsDocument());
            WriteXml(archive, "xl/workbook.xml", CreateWorkbookDocument(worksheetName));
            WriteXml(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelationshipsDocument(includeStyles));
            var worksheet = CreateWorksheetDocument(headers, rows);
            if (gridlines)
            {
                foreach (var cell in worksheet.Descendants(SpreadsheetNs + "c"))
                {
                    cell.SetAttributeValue("s", "1");
                }
            }
            foreach (var row in worksheet.Descendants(SpreadsheetNs + "row").Skip(1))
            {
                var cells = row.Elements(SpreadsheetNs + "c").ToArray();
                for (var index = 0; index < headers.Count; index++)
                {
                    if (!columnFormats.TryGetValue(headers[index], out var format)) continue;
                    var cell = cells[index];
                    cell.SetAttributeValue("s", 2 + formats.IndexOf(format) * 2 + (gridlines ? 1 : 0));
                    var value = cell.Element(SpreadsheetNs + "is")?.Element(SpreadsheetNs + "t")?.Value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        cell.RemoveNodes();
                        cell.SetAttributeValue("t", "n");
                    }
                    else if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
                             decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    {
                        cell.RemoveNodes();
                        cell.SetAttributeValue("t", "n");
                        cell.Add(new XElement(SpreadsheetNs + "v", number.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }
            if (includeStyles) WriteXml(archive, "xl/styles.xml", CreateGridlineStylesDocument(formats));
            WriteXml(archive, "xl/worksheets/sheet1.xml", worksheet);
        }

        return output.ToArray();
    }

    public byte[] CreateSemicolonCsv(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        bool windows1257 = false,
        IReadOnlySet<string>? numericColumns = null,
        string? valueCulture = null)
    {
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("Eksportui nerasta stulpeliu.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("sep=;");
        builder.AppendLine(string.Join(';', headers.Select(header => EscapeCsvValue(header, windows1257))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', headers.Select(header =>
                EscapeCsvValue(row.TryGetValue(header, out var value)
                    ? (windows1257 && numericColumns?.Contains(header) == true
                        ? CsvExportEncoding.InvariantNumber(value, valueCulture) : value)
                    : string.Empty, windows1257))));
        }

        return CsvExportEncoding.Encode(builder.ToString(), windows1257);
    }
    public ExcelWorksheetData ReadWorkbook(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = ResolveFirstWorksheetEntry(archive)
            ?? throw new InvalidDataException("Excel faile nerastas pirmas worksheet.");

        var sharedStrings = LoadSharedStrings(archive);
        var worksheet = LoadXml(worksheetEntry);
        var sheetData = worksheet.Root?.Element(SpreadsheetNs + "sheetData")
            ?? throw new InvalidDataException("Excel faile nerasti worksheet duomenys.");

        var parsedRows = sheetData
            .Elements(SpreadsheetNs + "row")
            .Select(row => ReadRow(row, sharedStrings))
            .Select(TrimTrailingEmptyCells)
            .Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();

        if (parsedRows.Length == 0)
        {
            throw new InvalidDataException("Excel failas neturi header eilutes.");
        }

        var headers = parsedRows[0]
            .Select(header => header.Trim())
            .ToArray();

        var dataRows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var row in parsedRows.Skip(1))
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                values[headers[index]] = index < row.Count ? row[index] : string.Empty;
            }

            if (values.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                dataRows.Add(values);
            }
        }

        return new ExcelWorksheetData(headers, dataRows);
    }

    private static string EscapeCsvValue(string? value, bool quoteAll = false)
    {
        var text = value ?? string.Empty;
        if (!quoteAll && !text.Contains(';', StringComparison.Ordinal) &&
            !text.Contains('"', StringComparison.Ordinal) &&
            !text.Contains('\r', StringComparison.Ordinal) &&
            !text.Contains('\n', StringComparison.Ordinal))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
    private static XDocument CreateGridlineStylesDocument(IReadOnlyList<string> formats)
    {
        var document = XDocument.Parse("""
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
              <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
              <borders count="2">
                <border><left/><right/><top/><bottom/><diagonal/></border>
                <border>
                  <left style="thin"><color rgb="FF000000"/></left>
                  <right style="thin"><color rgb="FF000000"/></right>
                  <top style="thin"><color rgb="FF000000"/></top>
                  <bottom style="thin"><color rgb="FF000000"/></bottom>
                  <diagonal/>
                </border>
              </borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="2">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/>
              </cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """);
        if (formats.Count > 0)
        {
            document.Root!.AddFirst(new XElement(SpreadsheetNs + "numFmts",
                new XAttribute("count", formats.Count),
                formats.Select((format, index) => new XElement(SpreadsheetNs + "numFmt",
                    new XAttribute("numFmtId", 164 + index), new XAttribute("formatCode", format)))));
            var cellFormats = document.Root.Element(SpreadsheetNs + "cellXfs")!;
            for (var index = 0; index < formats.Count; index++)
                for (var border = 0; border <= 1; border++)
                    cellFormats.Add(new XElement(SpreadsheetNs + "xf",
                        new XAttribute("numFmtId", 164 + index), new XAttribute("fontId", 0),
                        new XAttribute("fillId", 0), new XAttribute("borderId", border),
                        new XAttribute("xfId", 0), new XAttribute("applyNumberFormat", 1),
                        new XAttribute("applyBorder", border)));
            cellFormats.SetAttributeValue("count", 2 + formats.Count * 2);
        }
        return document;
    }

    private static XDocument CreateContentTypesDocument(bool gridlines)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ContentTypesNs + "Types",
                new XElement(ContentTypesNs + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypesNs + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                gridlines ? new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")) : null,
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", ContentType)),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
    }

    private static XDocument CreateRootRelationshipsDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(PackageRelationshipNs + "Relationships",
                new XElement(PackageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument CreateWorkbookRelationshipsDocument(bool gridlines)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(PackageRelationshipNs + "Relationships",
                gridlines ? new XElement(PackageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml")) : null,
                new XElement(PackageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));
    }

    private static XDocument CreateWorkbookDocument(string worksheetName)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(SpreadsheetNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", OfficeRelationshipNs),
                new XElement(SpreadsheetNs + "sheets",
                    new XElement(SpreadsheetNs + "sheet",
                        new XAttribute("name", NormalizeWorksheetName(worksheetName)),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(OfficeRelationshipNs + "id", "rId1")))));
    }

    private static XDocument CreateWorksheetDocument(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
    {
        var lastColumn = GetColumnName(headers.Count);
        var lastRow = Math.Max(1, rows.Count + 1);

        var sheetRows = new List<XElement>
        {
            CreateRow(1, headers, header => header)
        };

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            sheetRows.Add(CreateRow(rowIndex + 2, headers, header =>
                row.TryGetValue(header, out var value) ? value ?? string.Empty : string.Empty));
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(SpreadsheetNs + "worksheet",
                new XElement(SpreadsheetNs + "dimension",
                    new XAttribute("ref", $"A1:{lastColumn}{lastRow.ToString(CultureInfo.InvariantCulture)}")),
                new XElement(SpreadsheetNs + "sheetData", sheetRows),
                new XElement(SpreadsheetNs + "autoFilter",
                    new XAttribute("ref", $"A1:{lastColumn}1"))));
    }

    private static XElement CreateRow(
        int rowNumber,
        IReadOnlyList<string> headers,
        Func<string, string?> valueSelector)
    {
        return new XElement(SpreadsheetNs + "row",
            new XAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture)),
            headers.Select((header, index) => CreateCell(rowNumber, index + 1, valueSelector(header))));
    }

    private static XElement CreateCell(int rowNumber, int columnNumber, string? value)
    {
        return new XElement(SpreadsheetNs + "c",
            new XAttribute("r", $"{GetColumnName(columnNumber)}{rowNumber.ToString(CultureInfo.InvariantCulture)}"),
            new XAttribute("t", "inlineStr"),
            new XElement(SpreadsheetNs + "is",
                new XElement(SpreadsheetNs + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    value ?? string.Empty)));
    }

    private static ZipArchiveEntry? ResolveFirstWorksheetEntry(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
        {
            return archive.GetEntry("xl/worksheets/sheet1.xml");
        }

        var workbook = LoadXml(workbookEntry);
        var firstSheet = workbook.Root?
            .Element(SpreadsheetNs + "sheets")?
            .Elements(SpreadsheetNs + "sheet")
            .FirstOrDefault();

        var relationshipId = firstSheet?.Attribute(OfficeRelationshipNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return archive.GetEntry("xl/worksheets/sheet1.xml");
        }

        var relationships = LoadXml(relationshipsEntry);
        var target = relationships.Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .FirstOrDefault(relationship =>
                relationship.Attribute("Id")?.Value.Equals(relationshipId, StringComparison.OrdinalIgnoreCase) == true)?
            .Attribute("Target")?
            .Value;

        if (string.IsNullOrWhiteSpace(target))
        {
            return archive.GetEntry("xl/worksheets/sheet1.xml");
        }

        var entryName = target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : $"xl/{target}";

        return archive.GetEntry(entryName.Replace('\\', '/'));
    }

    private static IReadOnlyList<string> LoadSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        var document = LoadXml(sharedStringsEntry);
        return document.Root?
            .Elements(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToArray() ?? [];
    }

    private static IReadOnlyList<string> ReadRow(
        XElement row,
        IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        var fallbackColumnIndex = 1;

        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
            var columnIndex = GetColumnIndex(
                cell.Attribute("r")?.Value,
                fallbackColumnIndex);

            cells[columnIndex] = ReadCellValue(cell, sharedStrings);
            fallbackColumnIndex = columnIndex + 1;
        }

        if (cells.Count == 0)
        {
            return [];
        }

        var maxColumn = cells.Keys.Max();
        var values = new string[maxColumn];
        foreach (var (columnIndex, value) in cells)
        {
            values[columnIndex - 1] = value;
        }

        return values;
    }

    private static string ReadCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type?.Equals("inlineStr", StringComparison.OrdinalIgnoreCase) == true)
        {
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
        }

        var rawValue = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        if (type?.Equals("s", StringComparison.OrdinalIgnoreCase) == true &&
            int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex) &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        if (type?.Equals("b", StringComparison.OrdinalIgnoreCase) == true)
        {
            return rawValue == "1" ? "TRUE" : "FALSE";
        }

        return NormalizeNumericCellValue(rawValue);
    }

    private static string NormalizeNumericCellValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        if (double.TryParse(
            rawValue,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var doubleValue))
        {
            return Convert.ToDecimal(doubleValue).ToString("G29", LithuanianCulture);
        }

        return rawValue;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void WriteXml(ZipArchive archive, string entryName, XDocument document)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = false
        });

        document.Save(writer);
    }

    private static IReadOnlyList<string> TrimTrailingEmptyCells(IReadOnlyList<string> values)
    {
        var lastIndex = values.Count - 1;
        while (lastIndex >= 0 && string.IsNullOrWhiteSpace(values[lastIndex]))
        {
            lastIndex--;
        }

        if (lastIndex < 0)
        {
            return [];
        }

        return values.Take(lastIndex + 1).ToArray();
    }

    private static string NormalizeWorksheetName(string worksheetName)
    {
        var invalidCharacters = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleanName = invalidCharacters.Aggregate(
            string.IsNullOrWhiteSpace(worksheetName) ? "Data" : worksheetName,
            (current, invalidCharacter) => current.Replace(invalidCharacter, '_'));

        return cleanName.Length > 31 ? cleanName[..31] : cleanName;
    }

    private static string GetColumnName(int columnNumber)
    {
        var name = string.Empty;
        while (columnNumber > 0)
        {
            columnNumber--;
            name = (char)('A' + columnNumber % 26) + name;
            columnNumber /= 26;
        }

        return name;
    }

    private static int GetColumnIndex(string? cellReference, int fallbackColumnIndex)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return fallbackColumnIndex;
        }

        var columnIndex = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            columnIndex = columnIndex * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return columnIndex == 0 ? fallbackColumnIndex : columnIndex;
    }
}


