using System.Globalization;
using System.Text.RegularExpressions;

namespace DATA_EXCHANGE_HUB.Services;

public static partial class MailSubjectTemplateRenderer
{
    public static string Render(string template, DateTime runAt)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        return SubjectTokenRegex().Replace(template, match => RenderToken(match.Groups["body"].Value, runAt));
    }

    private static string RenderToken(string body, DateTime runAt)
    {
        var parts = body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var head = parts[0].Split(':', 2);
        var token = head[0].Trim();
        var format = head.Length == 2 && !string.IsNullOrWhiteSpace(head[1]) ? head[1].Trim() : "yyyyMMdd";
        var options = parts.Skip(1)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var offset = ReadInt(options, "offset", 0);

        return token.ToLowerInvariant() switch
        {
            "date" => runAt.Date.AddDays(offset).ToString(format, CultureInfo.InvariantCulture),
            "month" => runAt.Date.AddMonths(offset).ToString(format, CultureInfo.InvariantCulture),
            "week" => FormatWeek(runAt.Date.AddDays(offset * 7), format),
            "periodfrom" => FormatPeriodBoundary(runAt, format, options, offset, start: true),
            "periodto" => FormatPeriodBoundary(runAt, format, options, offset, start: false),
            _ => "{" + body + "}"
        };
    }

    private static string FormatPeriodBoundary(DateTime runAt, string format, IReadOnlyDictionary<string, string> options, int offset, bool start)
    {
        var period = options.TryGetValue("period", out var value) ? value.Trim().ToLowerInvariant() : "day";
        var date = runAt.Date;
        var boundary = period switch
        {
            "month" => MonthBoundary(date.AddMonths(offset), start),
            "week" => WeekBoundary(date.AddDays(offset * 7), start),
            _ => date.AddDays(offset)
        };
        return boundary.ToString(format, CultureInfo.InvariantCulture);
    }

    private static DateTime MonthBoundary(DateTime date, bool start)
    {
        var first = new DateTime(date.Year, date.Month, 1);
        return start ? first : first.AddMonths(1).AddDays(-1);
    }

    private static DateTime WeekBoundary(DateTime date, bool start)
    {
        var monday = date.AddDays(-(IsoDayOfWeek(date) - 1));
        return start ? monday : monday.AddDays(6);
    }

    private static string FormatWeek(DateTime date, string format)
    {
        var year = ISOWeek.GetYear(date);
        var week = ISOWeek.GetWeekOfYear(date);
        return format
            .Replace("yyyy", year.ToString("0000", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("ww", week.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static int IsoDayOfWeek(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    private static int ReadInt(IReadOnlyDictionary<string, string> options, string key, int fallback)
    {
        return options.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    [GeneratedRegex(@"\{(?<body>[^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex SubjectTokenRegex();
}
