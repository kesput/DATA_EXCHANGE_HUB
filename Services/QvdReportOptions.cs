namespace DATA_EXCHANGE_HUB.Services;

public sealed class QvdReportOptions
{
    public Dictionary<string, QvdReportSource> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int MaxDays { get; set; } = 31;
    public int MaxFiles { get; set; } = 256;
    public int MaxScannedRows { get; set; } = 10_000_000;
    public long MaxScanBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public int MaxInputRows { get; set; } = 1_000_000;
    public int MaxResultRows { get; set; } = 100_000;
    public int MaxColumns { get; set; } = 200;
    public long MaxFileBytes { get; set; } = 128 * 1024 * 1024;
    public long MaxTotalBytes { get; set; } = 256 * 1024 * 1024;
    public int TimeoutSeconds { get; set; } = 60;
    public int MemoryLimitMb { get; set; } = 256;
}

public sealed class QvdReportSource
{
    public string RootPath { get; set; } = "";
    public int? PreviewRootIndex { get; set; }
    public string[] Countries { get; set; } = [];
    public string[] Modules { get; set; } = [];
}

internal static class QvdReportError
{
    public static InvalidOperationException Create(string lt, string en, Exception? inner = null) =>
        new(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? en : lt, inner);
}
