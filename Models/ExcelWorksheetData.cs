namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExcelWorksheetData(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
