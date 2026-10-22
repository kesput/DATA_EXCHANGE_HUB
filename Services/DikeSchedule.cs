using System.Globalization;
using System.Text.RegularExpressions;

namespace DATA_EXCHANGE_HUB.Services;

public sealed record DikePlannedSend(DateTimeOffset At, DateOnly Period, bool ClockAdjusted);

public static class DikeSchedule
{
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    public static DikePlannedSend? Next(string firstPeriod, string frequency, string monthDays,
        string sendTime, string validFrom, DateTimeOffset now)
        => Find(firstPeriod, frequency, monthDays, sendTime, validFrom, now, previous: false);

    public static DikePlannedSend? Previous(string firstPeriod, string frequency, string monthDays,
        string sendTime, string validFrom, DateTimeOffset now)
        => Find(firstPeriod, frequency, monthDays, sendTime, validFrom, now, previous: true);

    private static DikePlannedSend? Find(string firstPeriod, string frequency, string monthDays,
        string sendTime, string validFrom, DateTimeOffset now, bool previous)
    {
        if (!Regex.IsMatch(firstPeriod, "^[0-9]{4}-?(0[1-9]|1[0-2])$")) return null;
        var compact = firstPeriod.Replace("-", "");
        if (!DateOnly.TryParseExact(compact + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ||
            !DateOnly.TryParseExact(validFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var valid) ||
            !TimeOnly.TryParseExact(sendTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        var step = frequency switch { "Kas menesi" => 1, "Kas ketvirti" => 3, _ => 0 };
        if (step == 0) return null;
        var parts = monthDays.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(value => !int.TryParse(value, out var day) || day < 1 || day > 31)) return null;
        var days = parts.Select(int.Parse).Distinct().Order().ToArray();
        var month = first;
        var localNow = TimeZoneInfo.ConvertTime(now, Vilnius);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var lower = previous ? first : today;
        if (valid > lower) lower = valid;
        var target = previous ? today : lower;
        var distance = (target.Year - month.Year) * 12 + target.Month - month.Month;
        if (previous && distance < 0) return null;
        if (distance > 0) month = month.AddMonths(distance / step * step);

        for (var attempt = 0; attempt < 48; attempt++)
        {
            foreach (var day in previous ? days.Reverse() : days)
            {
                if (day > DateTime.DaysInMonth(month.Year, month.Month)) continue;
                var date = new DateOnly(month.Year, month.Month, day);
                if (date < lower) continue;
                var local = date.ToDateTime(time, DateTimeKind.Unspecified);
                var adjusted = Vilnius.IsInvalidTime(local);
                // A clock jump must not silently remove the selected calendar day.
                if (adjusted) local = local.AddHours(1);
                var offsets = Vilnius.IsAmbiguousTime(local) ? Vilnius.GetAmbiguousTimeOffsets(local) : [Vilnius.GetUtcOffset(local)];
                var candidates = offsets.Select(offset => new DateTimeOffset(local, offset)).Order();
                foreach (var candidate in previous ? candidates.Reverse() : candidates)
                    if (previous ? candidate < now : candidate >= now) return new(candidate, month, adjusted);
            }
            if (previous)
            {
                if ((month.Year - first.Year) * 12 + month.Month - first.Month < step || month < lower.AddDays(1 - lower.Day)) break;
                month = month.AddMonths(-step);
                continue;
            }
            if ((9999 - month.Year) * 12 + 12 - month.Month < step) break;
            month = month.AddMonths(step);
        }
        return null;
    }
}
