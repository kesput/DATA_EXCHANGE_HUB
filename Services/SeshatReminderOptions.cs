namespace DATA_EXCHANGE_HUB.Services;

public sealed class SeshatReminderOptions
{
    public const string SectionName = "SeshatReminders";

    public int SendHour { get; set; } = 12;

    public int CheckEveryMinutes { get; set; } = 15;

    public int NormalizedSendHour => Math.Clamp(SendHour, 0, 23);

    public int NormalizedCheckEveryMinutes => Math.Clamp(CheckEveryMinutes, 1, 1440);

    public string SendTimeText => $"{NormalizedSendHour:00}:00";
}
