using System.Globalization;
using System.Text;

namespace DATA_EXCHANGE_HUB.Services;

public static class CsvExportEncoding
{
    // SQL numbers were rendered with this culture when the result was cached.
    // Only known numeric columns use this conversion; identifiers remain text.
    public static string? InvariantNumber(string? value, string? valueCulture)
    {
        if (value is null || valueCulture is null) return value;
        var separator = CultureInfo.GetCultureInfo(valueCulture).NumberFormat.NumberDecimalSeparator;
        return separator == "." ? value : value.Replace(separator, ".", StringComparison.Ordinal);
    }

    private static readonly Encoding Baltic = CreateBalticEncoding();

    private static Encoding CreateBalticEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1257, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static string ContentType(bool windows1257) =>
        windows1257 ? "text/csv; charset=windows-1257" : "text/csv; charset=utf-8";

    public static byte[] Encode(string text, bool windows1257 = false)
    {
        if (!windows1257)
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        try
        {
            return Baltic.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidOperationException(
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
                    ? "Some characters cannot be exported in Windows-1257. Clear the Windows-1257 option and export as UTF-8."
                    : "Kai kurių simbolių negalima eksportuoti Windows-1257 koduote. Nuimkite Windows-1257 varnelę ir eksportuokite UTF-8 formatu.",
                exception);
        }
    }
}
