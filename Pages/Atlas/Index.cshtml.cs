using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace DATA_EXCHANGE_HUB.Pages.Atlas;

public sealed class IndexModel(
    IAtlasService atlasService,
    IUserNameResolver userNameResolver,
    IStringLocalizer<DATA_EXCHANGE_HUB.SharedResource> localizer) : PageModel
{
    private static readonly string[] DefaultVisibleTypes = ["LEAVE", "WORK_FROM_HOME", "MEETING"];
    private static readonly int[] AllowedViewMonths = [1, 3, 6, 12];

    public bool HasAccess { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public DateTime CurrentMonth { get; private set; }

    public IReadOnlyList<AtlasCalendarDay> CalendarDays { get; private set; } = [];

    public IReadOnlyList<AtlasCalendarMonth> CalendarMonths { get; private set; } = [];

    public IReadOnlyList<AtlasPlan> Plans { get; private set; } = [];

    public IReadOnlyList<AtlasHoliday> Holidays { get; private set; } = [];

    public IReadOnlyList<AtlasUser> AtlasUsers { get; private set; } = [];

    public IReadOnlyList<DateTime> MatrixDays { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public int ViewMonths { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "calendar";

    [BindProperty(SupportsGet = true)]
    public List<string> ShowTypes { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<string> ShowUsers { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public bool UserFilterApplied { get; set; }

    [BindProperty]
    public string StartDateText { get; set; } = UiFormat.Date(DateTime.Today);

    [BindProperty]
    public string EndDateText { get; set; } = UiFormat.Date(DateTime.Today);

    [BindProperty]
    public string PlanType { get; set; } = "LEAVE";

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Notes { get; set; } = string.Empty;

    [BindProperty]
    public string UserColor { get; set; } = "#388e3c";

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string PreviousMonthUrl => Url.Page(
        "/Atlas/Index",
        null,
        BuildRouteValues(IsMatrixView ? CurrentMonth.AddYears(-1) : CurrentMonth.AddMonths(-ViewMonthCount))) ?? string.Empty;

    public string NextMonthUrl => Url.Page(
        "/Atlas/Index",
        null,
        BuildRouteValues(IsMatrixView ? CurrentMonth.AddYears(1) : CurrentMonth.AddMonths(ViewMonthCount))) ?? string.Empty;

    public int ViewMonthCount => NormalizeViewMonths(ViewMonths);

    public bool IsMatrixView => NormalizeViewMode(ViewMode).Equals("matrix", StringComparison.OrdinalIgnoreCase);

    public string RangeLabel => ViewMonthCount == 1
        ? (IsMatrixView ? MatrixYearLabel : CurrentMonth.ToString("yyyy-MM"))
        : IsMatrixView
            ? MatrixYearLabel
            : $"{CurrentMonth:yyyy-MM} - {CurrentMonth.AddMonths(ViewMonthCount - 1):yyyy-MM}";

    public string DatePlaceholder => UiFormat.Date(DateTime.Today);

    public string MatrixYearLabel => CurrentMonth.Year.ToString(CultureInfo.InvariantCulture);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        try
        {
            if (!TryParsePlanDate(StartDateText, out var startDate) ||
                !TryParsePlanDate(EndDateText, out var endDate))
            {
                ErrorMessage = string.Format(localizer["Atlas.Error.InvalidDate"].Value, DatePlaceholder);
                return RedirectToPage(BuildRouteValues(GetSelectedMonth()));
            }

            if (endDate.Date < startDate.Date)
            {
                ErrorMessage = localizer["Atlas.Error.EndBeforeStart"];
                return RedirectToPage(BuildRouteValues(GetSelectedMonth()));
            }

            var insertedRows = await atlasService.CreatePlanAsync(
                new AtlasCreatePlan(startDate, endDate, PlanType, Title, Notes),
                UserName,
                HttpContext.RequestAborted);
            SuccessMessage = string.Format(localizer["Atlas.Success.Created"].Value, insertedRows);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(BuildRouteValues(GetSelectedMonth()));
    }

    public async Task<IActionResult> OnPostColorAsync()
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        try
        {
            await atlasService.UpdateUserColorAsync(UserName, UserColor, HttpContext.RequestAborted);
            SuccessMessage = localizer["Atlas.Success.ColorSaved"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(BuildRouteValues(GetSelectedMonth()));
    }

    public async Task<IActionResult> OnPostDeleteAsync(int planId)
    {
        await LoadAccessAsync();
        if (!HasAccess)
        {
            return Forbid();
        }

        try
        {
            await atlasService.DeletePlanAsync(planId, UserName, HttpContext.RequestAborted);
            SuccessMessage = localizer["Atlas.Success.Deleted"];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(BuildRouteValues(GetSelectedMonth()));
    }

    public bool IsTypeVisible(string planType)
    {
        return GetVisibleTypes().Contains(planType, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsUserVisible(string userName)
    {
        return GetVisibleUsers().Contains(userName, StringComparer.OrdinalIgnoreCase);
    }

    public string TypeLabel(string planType)
    {
        return planType.ToUpperInvariant() switch
        {
            "LEAVE" => localizer["Atlas.Type.Leave"],
            "WORK_FROM_HOME" => localizer["Atlas.Type.WorkFromHome"],
            "MEETING" => localizer["Atlas.Type.Meeting"],
            _ => planType
        };
    }

    public string TypeCssClass(string planType)
    {
        return planType.ToUpperInvariant() switch
        {
            "LEAVE" => "atlas-plan-leave",
            "WORK_FROM_HOME" => "atlas-plan-home",
            "MEETING" => "atlas-plan-meeting",
            _ => "atlas-plan-default"
        };
    }

    public IReadOnlyList<AtlasPlan> MatrixPlansFor(string userName, DateTime date)
    {
        var visibleTypes = GetVisibleTypes();
        return Plans
            .Where(plan => plan.PlanDate.Date == date.Date &&
                           plan.CreatedBy.Equals(userName, StringComparison.OrdinalIgnoreCase) &&
                           GetVisibleUsers().Contains(plan.CreatedBy, StringComparer.OrdinalIgnoreCase) &&
                           visibleTypes.Contains(plan.PlanType, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    public AtlasHoliday? HolidayFor(DateTime date)
    {
        return Holidays.FirstOrDefault(holiday => holiday.Date.Date == date.Date);
    }

    public string HolidayName(AtlasHoliday holiday)
    {
        return Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? holiday.NameEn
            : holiday.NameLt;
    }

    private async Task LoadAsync()
    {
        await LoadAccessAsync();
        CurrentMonth = GetSelectedMonth();
        var defaultDate = DateTime.Today > CurrentMonth ? DateTime.Today : CurrentMonth;
        StartDateText = UiFormat.Date(defaultDate);
        EndDateText = UiFormat.Date(defaultDate);

        if (!HasAccess)
        {
            CalendarMonths = BuildEmptyCalendar(CurrentMonth);
            CalendarDays = CalendarMonths.SelectMany(month => month.Days).ToArray();
            return;
        }

        var firstGridDate = GetCalendarStart(CurrentMonth);
        var lastMonth = CurrentMonth.AddMonths(ViewMonthCount - 1);
        var lastGridDate = GetCalendarStart(lastMonth).AddDays(41);
        if (IsMatrixView)
        {
            firstGridDate = new DateTime(CurrentMonth.Year, 1, 1);
            lastGridDate = new DateTime(CurrentMonth.Year, 12, 31);
        }

        Holidays = Enumerable.Range(CurrentMonth.Year - 1, 3)
            .SelectMany(atlasService.GetPublicHolidays)
            .ToArray();

        try
        {
            AtlasUsers = await atlasService.ListUsersAsync(
                UserName,
                HttpContext.RequestAborted);
        }
        catch
        {
            AtlasUsers = [];
        }

        try
        {
            Plans = await atlasService.ListPlansAsync(
                firstGridDate,
                lastGridDate,
                UserName,
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            ErrorMessage ??= ex.Message;
            Plans = [];
        }

        CalendarMonths = Enumerable.Range(0, ViewMonthCount)
            .Select(offset =>
            {
                var month = CurrentMonth.AddMonths(offset);
                return new AtlasCalendarMonth(
                    month,
                    BuildCalendar(month, GetCalendarStart(month), Plans, Holidays));
            })
            .ToArray();
        CalendarDays = CalendarMonths.SelectMany(month => month.Days).ToArray();
        MatrixDays = Enumerable.Range(0, new DateTime(CurrentMonth.Year, 12, 31).DayOfYear)
            .Select(offset => new DateTime(CurrentMonth.Year, 1, 1).AddDays(offset))
            .ToArray();
    }

    private async Task LoadAccessAsync()
    {
        UserName = userNameResolver.ResolveUserName(User);
        try
        {
            HasAccess = await atlasService.HasAccessAsync(User, HttpContext.RequestAborted);
        }
        catch
        {
            HasAccess = false;
        }
    }

    private IReadOnlyList<AtlasCalendarMonth> BuildEmptyCalendar(DateTime month)
    {
        return Enumerable.Range(0, ViewMonthCount)
            .Select(offset =>
            {
                var monthStart = month.AddMonths(offset);
                return new AtlasCalendarMonth(
                    monthStart,
                    BuildCalendar(monthStart, GetCalendarStart(monthStart), [], []));
            })
            .ToArray();
    }

    private IReadOnlyList<AtlasCalendarDay> BuildCalendar(
        DateTime month,
        DateTime firstGridDate,
        IReadOnlyList<AtlasPlan> plans,
        IReadOnlyList<AtlasHoliday> holidays)
    {
        var visibleTypes = GetVisibleTypes();
        var visibleUsers = GetVisibleUsers();
        var holidayLookup = holidays.ToDictionary(holiday => holiday.Date.Date);
        var plansLookup = plans
            .Where(plan => visibleTypes.Contains(plan.PlanType, StringComparer.OrdinalIgnoreCase) &&
                           visibleUsers.Contains(plan.CreatedBy, StringComparer.OrdinalIgnoreCase))
            .GroupBy(plan => plan.PlanDate.Date)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<AtlasPlan>)group.ToArray());

        return Enumerable.Range(0, 42)
            .Select(offset =>
            {
                var date = firstGridDate.AddDays(offset);
                holidayLookup.TryGetValue(date.Date, out var holiday);
                plansLookup.TryGetValue(date.Date, out var datePlans);
                return new AtlasCalendarDay(
                    date,
                    date.Month == month.Month && date.Year == month.Year,
                    date.Date < DateTime.Today,
                    date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    holiday,
                    datePlans ?? []);
            })
            .ToArray();
    }

    private DateTime GetSelectedMonth()
    {
        var today = DateTime.Today;
        var year = Year.GetValueOrDefault(today.Year);
        var month = Month.GetValueOrDefault(today.Month);
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            return new DateTime(today.Year, today.Month, 1);
        }

        return new DateTime(year, month, 1);
    }

    private IReadOnlyList<string> GetVisibleTypes()
    {
        return ShowTypes.Count == 0
            ? DefaultVisibleTypes
            : ShowTypes
                .Where(type => DefaultVisibleTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private IReadOnlyList<string> GetVisibleUsers()
    {
        if (AtlasUsers.Count == 0)
        {
            return UserFilterApplied
                ? ShowUsers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }

        if (!UserFilterApplied)
        {
            return AtlasUsers.Select(user => user.UserName).ToArray();
        }

        var allowedUsers = AtlasUsers
            .Select(user => user.UserName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ShowUsers
            .Where(allowedUsers.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private Dictionary<string, string> BuildRouteValues(DateTime month)
    {
        var routeValues = new Dictionary<string, string>
        {
            ["Year"] = month.Year.ToString(),
            ["Month"] = month.Month.ToString(),
            ["ViewMonths"] = ViewMonthCount.ToString(),
            ["ViewMode"] = NormalizeViewMode(ViewMode),
            ["UserFilterApplied"] = UserFilterApplied.ToString()
        };

        var visibleTypes = GetVisibleTypes();
        for (var index = 0; index < visibleTypes.Count; index++)
        {
            routeValues[$"ShowTypes[{index}]"] = visibleTypes[index];
        }

        var visibleUsers = GetVisibleUsers();
        for (var index = 0; index < visibleUsers.Count; index++)
        {
            routeValues[$"ShowUsers[{index}]"] = visibleUsers[index];
        }

        return routeValues;
    }

    private static int NormalizeViewMonths(int viewMonths)
    {
        return AllowedViewMonths.Contains(viewMonths) ? viewMonths : 1;
    }

    private static string NormalizeViewMode(string? viewMode)
    {
        return viewMode?.Equals("matrix", StringComparison.OrdinalIgnoreCase) == true
            ? "matrix"
            : "calendar";
    }

    private static bool TryParsePlanDate(string? rawValue, out DateTime planDate)
    {
        planDate = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var formats = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? new[] { "dd/MM/yyyy", "yyyy-MM-dd" }
            : ["yyyy-MM-dd", "dd/MM/yyyy"];

        return DateTime.TryParseExact(
            rawValue.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out planDate);
    }

    private static DateTime GetCalendarStart(DateTime month)
    {
        var firstDay = new DateTime(month.Year, month.Month, 1);
        var offset = ((int)firstDay.DayOfWeek + 6) % 7;
        return firstDay.AddDays(-offset);
    }
}
