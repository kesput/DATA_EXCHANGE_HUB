using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IExcelWorkbookService
{
    byte[] CreateWorkbook(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string worksheetName,
        bool gridlines = false,
        IReadOnlyDictionary<string, string>? numberFormats = null);

    byte[] CreateSemicolonCsv(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        bool windows1257 = false,
        IReadOnlySet<string>? numericColumns = null,
        string? valueCulture = null);

    ExcelWorksheetData ReadWorkbook(Stream stream);
}

