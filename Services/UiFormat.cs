using System.Globalization;

namespace DATA_EXCHANGE_HUB.Services;

public static class UiFormat
{
    public static string Date(DateTime value)
    {
        return value.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    public static string Date(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    public static string DateTime(DateTime value)
    {
        return value.ToString($"{DateFormat} HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string DateTime(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString($"{DateFormat} HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string DateTimeMinute(DateTimeOffset value)
    {
        return value.LocalDateTime.ToString($"{DateFormat} HH:mm", CultureInfo.InvariantCulture);
    }

    private static string DateFormat =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? "dd/MM/yyyy"
            : "yyyy-MM-dd";
}
