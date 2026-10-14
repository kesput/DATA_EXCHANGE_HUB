namespace DATA_EXCHANGE_HUB.Models;

public sealed record AtlasPlan(
    int Id,
    DateTime PlanDate,
    string PlanType,
    string Title,
    string Notes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string UserColor,
    bool CanDelete);

public sealed record AtlasCreatePlan(
    DateTime StartDate,
    DateTime EndDate,
    string PlanType,
    string Title,
    string Notes);

public sealed record AtlasUser(
    string UserName,
    string Color,
    bool IsCurrentUser);

public sealed record AtlasHoliday(
    DateTime Date,
    string NameLt,
    string NameEn);

public sealed record AtlasCalendarDay(
    DateTime Date,
    bool IsCurrentMonth,
    bool IsPast,
    bool IsWeekend,
    AtlasHoliday? Holiday,
    IReadOnlyList<AtlasPlan> Plans);

public sealed record AtlasCalendarMonth(
    DateTime MonthStart,
    IReadOnlyList<AtlasCalendarDay> Days);
