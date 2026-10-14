using System.Globalization;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Iris;

public class IndexModel(
    ISendMailService sendMailService,
    IUserNameResolver userNameResolver,
    IIrisService irisService,
    IConfiguration configuration) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long SelectedReportId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ActiveTab { get; set; } = "builder";

    [BindProperty]
    public string SenderKey { get; set; } = "bls";

    [BindProperty]
    public string ApplicationName { get; set; } = "IRIS";

    [BindProperty]
    public string ProcessName { get; set; } = "Informacinis pranesimas";

    [BindProperty]
    public string RecipientEmail { get; set; } = string.Empty;

    [BindProperty]
    public string RecipientName { get; set; } = string.Empty;

    [BindProperty]
    public string Message { get; set; } = "Siunciame aktualia informacija ir rodikliu atnaujinima.";

    public SendMailDraft? Draft { get; private set; }

    public SendMailArchiveResult? ArchiveResult { get; private set; }

    public IReadOnlyList<SendMailLogEntry> RecentMails { get; private set; } = [];

    public IReadOnlyList<SendMailSenderProfile> SenderProfiles { get; private set; } = [];

    public string ArchiveRoot { get; private set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public string? SuccessMessage { get; private set; }

    public IReadOnlyList<IrisReportSummary> Reports { get; private set; } = [];

    public IReadOnlyList<IrisReportSource> SelectedReportSources { get; private set; } = [];

    public IReadOnlyList<IrisReportRecipient> SelectedReportRecipients { get; private set; } = [];

    public IReadOnlyList<IrisSchedule> SelectedReportSchedules { get; private set; } = [];

    public IReadOnlyList<string> SqlConnectionNames { get; private set; } = [];

    [BindProperty]
    public string NewKpiName { get; set; } = string.Empty;

    [BindProperty]
    public string NewReportTitle { get; set; } = string.Empty;

    [BindProperty]
    public string NewReportSubjectTemplate { get; set; } = string.Empty;

    [BindProperty]
    public string NewReportSenderKey { get; set; } = "bls";

    [BindProperty]
    public string NewReportSqlConnectionName { get; set; } = "DataExchangeSql";

    [BindProperty]
    public long SourceReportId { get; set; }

    [BindProperty]
    public long SourceId { get; set; }

    [BindProperty]
    public string SourceType { get; set; } = "INLINE_SQL";

    [BindProperty]
    public string SourceSqlConnectionName { get; set; } = "DataExchangeSql";

    [BindProperty]
    public string SourceSqlDatabase { get; set; } = string.Empty;

    [BindProperty]
    public string SourceSqlText { get; set; } = string.Empty;

    [BindProperty]
    public string SourceSqlFilePath { get; set; } = string.Empty;

    [BindProperty]
    public string SourceStoredProcedure { get; set; } = string.Empty;

    [BindProperty]
    public string SourceTableTitle { get; set; } = string.Empty;

    [BindProperty]
    public bool SourceIncludeHtmlTable { get; set; } = true;

    [BindProperty]
    public bool SourceAttachExcel { get; set; }

    [BindProperty]
    public bool SourceAttachCsv { get; set; }

    [BindProperty]
    public long RecipientReportId { get; set; }

    [BindProperty]
    public string RecipientAddressType { get; set; } = "TO";

    [BindProperty]
    public string RecipientEmailAddress { get; set; } = string.Empty;

    [BindProperty]
    public string RecipientDisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string RecipientErp { get; set; } = string.Empty;

    [BindProperty]
    public long ScheduleReportId { get; set; }

    [BindProperty]
    public long ScheduleId { get; set; }

    [BindProperty]
    public string ScheduleType { get; set; } = "MANUAL";

    [BindProperty]
    public string ScheduleTime { get; set; } = "08:00";

    [BindProperty]
    public string ScheduleStartDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [BindProperty]
    public int ScheduleHour { get; set; } = 8;

    [BindProperty]
    public int ScheduleMinute { get; set; }

    [BindProperty]
    public string ScheduleOnceAt { get; set; } = string.Empty;

    [BindProperty]
    public int ScheduleDailyInterval { get; set; } = 1;

    [BindProperty]
    public int ScheduleWeeklyInterval { get; set; } = 1;

    [BindProperty]
    public int ScheduleDayOfWeek { get; set; } = 1;

    [BindProperty]
    public List<int> ScheduleDaysOfWeek { get; set; } = [];

    [BindProperty]
    public List<int> ScheduleMonths { get; set; } = [];

    [BindProperty]
    public int ScheduleDayOfMonth { get; set; } = 1;

    [BindProperty]
    public bool ScheduleLastDayOfMonth { get; set; }

    [BindProperty]
    public bool ScheduleNoEndDate { get; set; } = true;

    [BindProperty]
    public string ScheduleEndAt { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        await LoadPageStateAsync();
    }

    public async Task OnPostPreviewAsync()
    {
        try
        {
            Draft = BuildDraft();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostSaveDraftAsync()
    {
        try
        {
            Draft = BuildDraft();
            ArchiveResult = await sendMailService.SaveDraftAsync(Draft, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostCreateReportAsync()
    {
        ActiveTab = "control";
        try
        {
            var id = await irisService.CreateReportAsync(
                new IrisCreateReportRequest(
                    NewKpiName,
                    NewReportTitle,
                    NewReportSubjectTemplate,
                    NewReportSenderKey,
                    NewReportSqlConnectionName,
                    userNameResolver.ResolveUserName(User)),
                HttpContext.RequestAborted);
            SuccessMessage = $"IRIS taskas sukurtas. Id={id}";
            SelectedReportId = id;
            SourceReportId = id;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostCreateSourceAsync()
    {
        ActiveTab = "control";
        SelectedReportId = SourceReportId;
        try
        {
            var id = await irisService.CreateSourceAsync(
                new IrisCreateSourceRequest(
                    SourceReportId,
                    SourceType,
                    SourceSqlConnectionName,
                    SourceSqlDatabase,
                    SourceSqlText,
                    SourceSqlFilePath,
                    SourceStoredProcedure,
                    SourceTableTitle,
                    SourceIncludeHtmlTable,
                    SourceAttachExcel,
                    SourceAttachCsv,
                    userNameResolver.ResolveUserName(User)),
                HttpContext.RequestAborted);
            SuccessMessage = $"SQL saltinis pridetas. Id={id}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostLoadSourceAsync(long sourceId)
    {
        ActiveTab = "control";
        try
        {
            await LoadPageStateAsync();
            var source = SelectedReportSources.FirstOrDefault(item => item.Id == sourceId);
            if (source is null)
            {
                throw new InvalidOperationException("SQL saltinis nerastas.");
            }

            SourceId = source.Id;
            SourceReportId = source.ReportId;
            SelectedReportId = source.ReportId;
            SourceType = source.SourceType;
            SourceSqlConnectionName = source.SqlConnectionName;
            SourceSqlDatabase = source.SqlDatabase;
            SourceSqlText = source.SqlText;
            SourceSqlFilePath = source.SqlFilePath;
            SourceStoredProcedure = source.StoredProcedure;
            SourceTableTitle = source.TableTitle;
            SourceIncludeHtmlTable = source.IncludeHtmlTable;
            SourceAttachExcel = source.AttachExcel;
            SourceAttachCsv = source.AttachCsv;
            SuccessMessage = $"SQL saltinis #{source.SourceOrder} ikeltas redagavimui.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageStateAsync();
        }
    }

    public async Task OnPostUpdateSourceAsync()
    {
        ActiveTab = "control";
        SelectedReportId = SourceReportId;
        try
        {
            await irisService.UpdateSourceAsync(
                new IrisUpdateSourceRequest(
                    SourceId,
                    SourceReportId,
                    SourceType,
                    SourceSqlConnectionName,
                    SourceSqlDatabase,
                    SourceSqlText,
                    SourceSqlFilePath,
                    SourceStoredProcedure,
                    SourceTableTitle,
                    SourceIncludeHtmlTable,
                    SourceAttachExcel,
                    SourceAttachCsv),
                HttpContext.RequestAborted);
            SuccessMessage = "SQL saltinis atnaujintas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostDeleteSourceAsync(long sourceId)
    {
        ActiveTab = "control";
        try
        {
            await irisService.DeleteSourceAsync(sourceId, HttpContext.RequestAborted);
            SuccessMessage = "SQL saltinis panaikintas.";
            SourceId = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostCreateRecipientAsync()
    {
        ActiveTab = "control";
        SelectedReportId = RecipientReportId;
        try
        {
            SourceReportId = RecipientReportId;
            var id = await irisService.CreateRecipientAsync(
                new IrisCreateRecipientRequest(
                    RecipientReportId,
                    RecipientAddressType,
                    RecipientEmailAddress,
                    RecipientDisplayName,
                    RecipientErp,
                    userNameResolver.ResolveUserName(User)),
                HttpContext.RequestAborted);
            SuccessMessage = $"Adresatas pridetas. Id={id}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostCreateScheduleAsync()
    {
        ActiveTab = "control";
        SelectedReportId = ScheduleReportId;
        try
        {
            SourceReportId = ScheduleReportId;
            var nextRunAt = CalculateNextRunAt();
            var json = BuildScheduleJson();
            var id = await irisService.CreateScheduleAsync(
                new IrisCreateScheduleRequest(
                    ScheduleReportId,
                    ScheduleType,
                    json,
                    nextRunAt,
                    userNameResolver.ResolveUserName(User)),
                HttpContext.RequestAborted);
            SuccessMessage = $"Grafikas pridetas. Id={id}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostLoadScheduleAsync(long scheduleId)
    {
        ActiveTab = "control";
        try
        {
            await LoadPageStateAsync();
            var schedule = SelectedReportSchedules.FirstOrDefault(item => item.Id == scheduleId);
            if (schedule is null)
            {
                throw new InvalidOperationException("Grafikas nerastas.");
            }

            ScheduleId = schedule.Id;
            ScheduleReportId = schedule.ReportId;
            SelectedReportId = schedule.ReportId;
            ScheduleType = schedule.ScheduleType;
            ApplyScheduleJson(schedule.ScheduleJson);
            SuccessMessage = "Grafikas ikeltas redagavimui.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPageStateAsync();
        }
    }

    public async Task OnPostUpdateScheduleAsync()
    {
        ActiveTab = "control";
        SelectedReportId = ScheduleReportId;
        try
        {
            var nextRunAt = CalculateNextRunAt();
            var json = BuildScheduleJson();
            await irisService.UpdateScheduleAsync(
                new IrisUpdateScheduleRequest(
                    ScheduleId,
                    ScheduleReportId,
                    ScheduleType,
                    json,
                    nextRunAt),
                HttpContext.RequestAborted);
            SuccessMessage = "Grafikas atnaujintas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostUpdateReportAsync()
    {
        ActiveTab = "control";
        try
        {
            await irisService.UpdateReportAsync(
                new IrisUpdateReportRequest(
                    SelectedReportId,
                    NewKpiName,
                    NewReportTitle,
                    NewReportSubjectTemplate,
                    NewReportSenderKey,
                    NewReportSqlConnectionName),
                HttpContext.RequestAborted);
            SuccessMessage = "IRIS taskas atnaujintas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        SourceReportId = SelectedReportId;
        await LoadPageStateAsync();
    }

    public async Task OnPostToggleRecipientAsync(long recipientId, bool isActive)
    {
        ActiveTab = "control";
        try
        {
            await irisService.SetRecipientActiveAsync(recipientId, isActive, HttpContext.RequestAborted);
            SuccessMessage = isActive ? "Adresatas aktyvuotas." : "Adresatas isjungtas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostToggleScheduleAsync(long scheduleId, bool isActive)
    {
        ActiveTab = "control";
        try
        {
            await irisService.SetScheduleActiveAsync(scheduleId, isActive, HttpContext.RequestAborted);
            SuccessMessage = isActive ? "Grafikas aktyvuotas." : "Grafikas isjungtas.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    public async Task OnPostDeleteScheduleAsync(long scheduleId)
    {
        ActiveTab = "control";
        try
        {
            await irisService.DeleteScheduleAsync(scheduleId, HttpContext.RequestAborted);
            SuccessMessage = "Grafikas panaikintas.";
            ScheduleId = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPageStateAsync();
    }

    private SendMailDraft BuildDraft()
    {
        return sendMailService.BuildDraft(
            ApplicationName,
            ProcessName,
            RecipientEmail,
            RecipientName,
            Message,
            userNameResolver.ResolveUserName(User),
            SenderKey);
    }

    private async Task LoadPageStateAsync()
    {
        SenderProfiles = sendMailService.ListSenderProfiles();
        SqlConnectionNames = configuration.GetSection("ConnectionStrings").GetChildren()
            .Select(section => section.Key)
            .OrderBy(key => key)
            .ToArray();
        if (!SenderProfiles.Any(profile => profile.Key.Equals(SenderKey, StringComparison.OrdinalIgnoreCase)))
        {
            SenderKey = SenderProfiles.FirstOrDefault()?.Key ?? string.Empty;
        }

        ArchiveRoot = sendMailService.GetArchiveRoot();
        RecentMails = await sendMailService.ListRecentAsync(HttpContext.RequestAborted);
        try
        {
            Reports = await irisService.ListReportsAsync(HttpContext.RequestAborted);
            var selectedReportId = SelectedReportId != 0
                ? SelectedReportId
                : SourceReportId != 0
                    ? SourceReportId
                    : RecipientReportId != 0
                        ? RecipientReportId
                        : ScheduleReportId != 0
                            ? ScheduleReportId
                            : Reports.FirstOrDefault()?.Id ?? 0;
            if (selectedReportId != 0)
            {
                SelectedReportId = selectedReportId;
                SourceReportId = selectedReportId;
                RecipientReportId = selectedReportId;
                ScheduleReportId = selectedReportId;
                var selectedReport = Reports.FirstOrDefault(report => report.Id == selectedReportId);
                if (selectedReport is not null)
                {
                    NewKpiName = selectedReport.KpiName;
                    NewReportTitle = selectedReport.Title;
                    NewReportSubjectTemplate = selectedReport.SubjectTemplate;
                    NewReportSenderKey = selectedReport.SenderKey;
                    NewReportSqlConnectionName = selectedReport.DefaultSqlConnectionName;
                }
                SelectedReportSources = await irisService.ListSourcesAsync(selectedReportId, HttpContext.RequestAborted);
                SelectedReportRecipients = await irisService.ListRecipientsAsync(selectedReportId, HttpContext.RequestAborted);
                SelectedReportSchedules = await irisService.ListSchedulesAsync(selectedReportId, HttpContext.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage ??= ex.Message;
        }
    }

    private DateTime? CalculateNextRunAt()
    {
        if (ScheduleType.Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ScheduleType.Equals("ONCE", StringComparison.OrdinalIgnoreCase))
        {
            var onceAt = BuildScheduleStartDateTime();
            return IsBeforeScheduleEnd(onceAt) ? onceAt : null;
        }

        var now = DateTime.Now;
        var startAt = BuildScheduleStartDateTime();
        var time = startAt.TimeOfDay;

        if (ScheduleType.Equals("DAILY", StringComparison.OrdinalIgnoreCase))
        {
            var interval = Math.Max(1, ScheduleDailyInterval);
            var candidate = startAt.Date.Add(time);
            while (candidate <= now)
            {
                candidate = candidate.AddDays(interval);
            }

            return IsBeforeScheduleEnd(candidate) ? candidate : null;
        }

        if (ScheduleType.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase))
        {
            var days = NormalizeDaysOfWeek();
            return Enumerable.Range(0, 14)
                .Select(offset => now.Date.AddDays(offset).Add(time))
                .Where(candidate => candidate > now && days.Contains(ToIsoDayOfWeek(candidate)) && IsBeforeScheduleEnd(candidate))
                .OrderBy(candidate => candidate)
                .FirstOrDefault();
        }

        if (ScheduleType.Equals("MONTHLY", StringComparison.OrdinalIgnoreCase))
        {
            var months = NormalizeMonths();
            return Enumerable.Range(0, 24)
                .Select(offset => now.AddMonths(offset))
                .Where(month => months.Contains(month.Month))
                .Select(month => BuildMonthlyCandidate(month.Year, month.Month, time))
                .Where(candidate => candidate > now && IsBeforeScheduleEnd(candidate))
                .OrderBy(candidate => candidate)
                .FirstOrDefault();
        }

        return null;
    }

    private DateTime BuildScheduleStartDateTime()
    {
        var date = DateTime.TryParseExact(
            ScheduleStartDate?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate)
            ? parsedDate.Date
            : DateTime.Today;
        var hour = Math.Clamp(ScheduleHour, 0, 23);
        var minute = Math.Clamp(ScheduleMinute, 0, 59);
        ScheduleTime = $"{hour:00}:{minute:00}";
        ScheduleOnceAt = $"{date:yyyy-MM-dd} {hour:00}:{minute:00}";
        return date.AddHours(hour).AddMinutes(minute);
    }
    private bool IsBeforeScheduleEnd(DateTime candidate)
    {
        if (ScheduleNoEndDate || string.IsNullOrWhiteSpace(ScheduleEndAt))
        {
            return true;
        }

        return TryParseScheduleEndAt(ScheduleEndAt, out var endAt) && candidate <= endAt;
    }

    private static bool TryParseScheduleEndAt(string value, out DateTime result)
    {
        return DateTime.TryParseExact(
                   value?.Trim(),
                   ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm"],
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out result) ||
               DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
    private DateTime BuildMonthlyCandidate(int year, int month, TimeSpan time)
    {
        var day = ScheduleLastDayOfMonth
            ? DateTime.DaysInMonth(year, month)
            : Math.Min(Math.Clamp(ScheduleDayOfMonth, 1, 31), DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day).Add(time);
    }

    private List<int> NormalizeDaysOfWeek()
    {
        var days = ScheduleDaysOfWeek
            .Where(day => day is >= 1 and <= 7)
            .Distinct()
            .OrderBy(day => day)
            .ToList();

        if (days.Count == 0)
        {
            days.Add(Math.Clamp(ScheduleDayOfWeek, 1, 7));
        }

        return days;
    }

    private List<int> NormalizeMonths()
    {
        var months = ScheduleMonths
            .Where(month => month is >= 1 and <= 12)
            .Distinct()
            .OrderBy(month => month)
            .ToList();

        return months.Count == 0 ? Enumerable.Range(1, 12).ToList() : months;
    }

    private static int ToIsoDayOfWeek(DateTime value) => ((int)value.DayOfWeek + 6) % 7 + 1;
    private static bool TryParseScheduleOnceAt(string value, out DateTime result)
    {
        return DateTime.TryParseExact(
                   value?.Trim(),
                   ["yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm"],
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out result) ||
               DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
    private string BuildScheduleJson()
    {
        var parts = new[]
        {
            $"type={ScheduleType}",
            $"time={ScheduleTime}",
            $"startDate={ScheduleStartDate}",
            $"hour={Math.Clamp(ScheduleHour, 0, 23)}",
            $"minute={Math.Clamp(ScheduleMinute, 0, 59)}",
            $"onceAt={ScheduleOnceAt}",
            $"dailyInterval={Math.Max(1, ScheduleDailyInterval)}",
            $"weeklyInterval={Math.Max(1, ScheduleWeeklyInterval)}",
            $"daysOfWeek={string.Join(',', NormalizeDaysOfWeek())}",
            $"months={string.Join(',', NormalizeMonths())}",
            $"dayOfMonth={ScheduleDayOfMonth}",
            $"lastDayOfMonth={ScheduleLastDayOfMonth}",
            $"noEndDate={ScheduleNoEndDate}",
            $"endAt={ScheduleEndAt}"
        };
        return string.Join(";", parts);
    }

    private void ApplyScheduleJson(string scheduleJson)
    {
        var values = scheduleJson
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        if (values.TryGetValue("startDate", out var startDate) && !string.IsNullOrWhiteSpace(startDate))
        {
            ScheduleStartDate = startDate;
        }

        if (values.TryGetValue("hour", out var hour) && int.TryParse(hour, out var parsedHour))
        {
            ScheduleHour = Math.Clamp(parsedHour, 0, 23);
        }

        if (values.TryGetValue("minute", out var minute) && int.TryParse(minute, out var parsedMinute))
        {
            ScheduleMinute = Math.Clamp(parsedMinute, 0, 59);
        }

        if (values.TryGetValue("dailyInterval", out var dailyInterval) && int.TryParse(dailyInterval, out var parsedDailyInterval))
        {
            ScheduleDailyInterval = Math.Max(1, parsedDailyInterval);
        }

        if (values.TryGetValue("weeklyInterval", out var weeklyInterval) && int.TryParse(weeklyInterval, out var parsedWeeklyInterval))
        {
            ScheduleWeeklyInterval = Math.Max(1, parsedWeeklyInterval);
        }

        if (values.TryGetValue("daysOfWeek", out var daysOfWeek))
        {
            ScheduleDaysOfWeek = ParseIntegerList(daysOfWeek, 1, 7);
            ScheduleDayOfWeek = ScheduleDaysOfWeek.FirstOrDefault(1);
        }

        if (values.TryGetValue("months", out var months))
        {
            ScheduleMonths = ParseIntegerList(months, 1, 12);
        }

        if (values.TryGetValue("dayOfMonth", out var dayOfMonth) && int.TryParse(dayOfMonth, out var parsedDayOfMonth))
        {
            ScheduleDayOfMonth = Math.Clamp(parsedDayOfMonth, 1, 31);
        }

        if (values.TryGetValue("lastDayOfMonth", out var lastDayOfMonth) && bool.TryParse(lastDayOfMonth, out var parsedLastDay))
        {
            ScheduleLastDayOfMonth = parsedLastDay;
        }

        if (values.TryGetValue("noEndDate", out var noEndDate) && bool.TryParse(noEndDate, out var parsedNoEndDate))
        {
            ScheduleNoEndDate = parsedNoEndDate;
        }

        if (values.TryGetValue("endAt", out var endAt))
        {
            ScheduleEndAt = endAt;
        }

        ScheduleTime = $"{ScheduleHour:00}:{ScheduleMinute:00}";
        ScheduleOnceAt = $"{ScheduleStartDate} {ScheduleHour:00}:{ScheduleMinute:00}";
    }

    private static List<int> ParseIntegerList(string value, int min, int max)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, out var parsed) ? parsed : 0)
            .Where(item => item >= min && item <= max)
            .Distinct()
            .OrderBy(item => item)
            .ToList();
    }
}





