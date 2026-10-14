using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IExcelWorkbookService
{
    byte[] CreateWorkbook(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string worksheetName);

    byte[] CreateSemicolonCsv(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows);

    ExcelWorksheetData ReadWorkbook(Stream stream);
}

