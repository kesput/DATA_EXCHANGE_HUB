namespace DATA_EXCHANGE_HUB.Services;

public static class ExcelFileNames
{
    public const string UploadAccept = ".xlsx,.xlsm,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel.sheet.macroEnabled.12";

    public static bool IsSupportedUpload(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);
    }
}
