using System.Data;
using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class AtlasService(
    IConfiguration configuration,
    IOptions<AtlasOptions> options,
    IUserNameResolver userNameResolver) : IAtlasService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private const string DefaultPlanTableName = "BLS_LOGIT_STAT.dbo.ATLAS_PLANS";
    private const string DefaultAccessTableName = "BLS_LOGIT_STAT.dbo.BS_APP_ACCESS";
    private const string DefaultUserSettingsTableName = "BLS_LOGIT_STAT.dbo.ATLAS_USER_SETTINGS";
    private const string DefaultUserColor = "#388e3c";
    private readonly AtlasOptions _options = options.Value;

    public async Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            DefaultAccessProcedureName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AtlasPlan>> ListPlansAsync(
        DateTime fromDate,
        DateTime toDate,
        string currentUserName,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizePlanTableName();
        var settingsTableName = NormalizeUserSettingsTableName();
        await using var connection = new SqlConnection(GetConnectionString(_options.DataConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ID,
                PLAN_DATE,
                PLAN_TYPE,
                TITLE,
                NOTES,
                CREATED_BY,
                CREATED_AT,
                COALESCE(NULLIF(settings.COLOR, N''), @DefaultColor) AS USER_COLOR
            FROM {tableName} plans
            LEFT JOIN {settingsTableName} settings
                ON settings.DOMEN_USER = plans.CREATED_BY
            WHERE PLAN_DATE >= @FromDate
              AND PLAN_DATE <= @ToDate
            ORDER BY PLAN_DATE, CREATED_BY, ID
            """;
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.Date;
        command.Parameters.Add("@DefaultColor", SqlDbType.NVarChar, 20).Value = DefaultUserColor;

        var plans = new List<AtlasPlan>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var planDate = reader.GetDateTime(1).Date;
            var createdBy = reader.GetString(5);
            plans.Add(new AtlasPlan(
                reader.GetInt32(0),
                planDate,
                reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                createdBy,
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetString(7),
                CanDeletePlan(planDate, createdBy, currentUserName)));
        }

        return plans;
    }

    public async Task<int> CreatePlanAsync(
        AtlasCreatePlan plan,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCreatePlan(plan);
        if (normalized.StartDate.Date < DateTime.Today)
        {
            throw new InvalidOperationException("Galima planuoti tik siandienos arba ateities dienas.");
        }

        if (normalized.EndDate.Date < normalized.StartDate.Date)
        {
            throw new InvalidOperationException("Periodo pabaiga negali buti ankstesne uz pradzia.");
        }

        var tableName = NormalizePlanTableName();
        await using var connection = new SqlConnection(GetConnectionString(_options.DataConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        var insertedRows = 0;
        foreach (var planDate in EnumeratePlanDates(normalized.StartDate, normalized.EndDate))
        {
            if (planDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                GetPublicHolidays(planDate.Year).Any(holiday => holiday.Date == planDate.Date))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {tableName}
                (
                    PLAN_DATE,
                    PLAN_TYPE,
                    TITLE,
                    NOTES,
                    CREATED_BY,
                    CREATED_AT,
                    UPDATED_AT
                )
                SELECT
                    @PlanDate,
                    @PlanType,
                    @Title,
                    @Notes,
                    @CreatedBy,
                    SYSDATETIMEOFFSET(),
                    SYSDATETIMEOFFSET()
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM {tableName}
                    WHERE PLAN_DATE = @PlanDate
                      AND PLAN_TYPE = @PlanType
                      AND CREATED_BY = @CreatedBy
                )
                """;
            command.Parameters.Add("@PlanDate", SqlDbType.Date).Value = planDate.Date;
            command.Parameters.Add("@PlanType", SqlDbType.NVarChar, 50).Value = normalized.PlanType;
            command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(normalized.Title) ? DBNull.Value : normalized.Title;
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(normalized.Notes) ? DBNull.Value : normalized.Notes;
            command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 256).Value = NormalizeActor(actorUserName);
            insertedRows += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (insertedRows == 0)
        {
            throw new InvalidOperationException("Pasirinktame periode nerasta nauju darbo dienu planui sukurti.");
        }

        return insertedRows;
    }

    public async Task<IReadOnlyList<AtlasUser>> ListUsersAsync(
        string currentUserName,
        CancellationToken cancellationToken = default)
    {
        var accessTableName = NormalizeAccessTableName();
        var settingsTableName = NormalizeUserSettingsTableName();
        await using var connection = new SqlConnection(GetConnectionString(_options.AccessConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT DISTINCT
                access.DOMEN_USER,
                COALESCE(NULLIF(settings.COLOR, N''), @DefaultColor) AS COLOR
            FROM {accessTableName} access
            LEFT JOIN {settingsTableName} settings
                ON settings.DOMEN_USER = access.DOMEN_USER
            WHERE access.APP_NAME = @AppName
              AND ISNULL(access.ACTIVE, 1) = 1
            ORDER BY access.DOMEN_USER
            """;
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@DefaultColor", SqlDbType.NVarChar, 20).Value = DefaultUserColor;

        var users = new List<AtlasUser>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userName = reader.GetString(0);
            users.Add(new AtlasUser(
                userName,
                reader.GetString(1),
                NormalizeActor(userName).Equals(NormalizeActor(currentUserName), StringComparison.OrdinalIgnoreCase)));
        }

        return users;
    }

    public async Task UpdateUserColorAsync(
        string actorUserName,
        string color,
        CancellationToken cancellationToken = default)
    {
        var normalizedColor = NormalizeColor(color);
        var settingsTableName = NormalizeUserSettingsTableName();
        await using var connection = new SqlConnection(GetConnectionString(_options.DataConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            MERGE {settingsTableName} AS target
            USING (SELECT @UserName AS DOMEN_USER, @Color AS COLOR) AS source
                ON target.DOMEN_USER = source.DOMEN_USER
            WHEN MATCHED THEN
                UPDATE SET COLOR = source.COLOR, UPDATED_AT = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN
                INSERT (DOMEN_USER, COLOR, UPDATED_AT)
                VALUES (source.DOMEN_USER, source.COLOR, SYSDATETIMEOFFSET());
            """;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = NormalizeActor(actorUserName);
        command.Parameters.Add("@Color", SqlDbType.NVarChar, 20).Value = normalizedColor;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeletePlanAsync(
        int planId,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var tableName = NormalizePlanTableName();
        await using var connection = new SqlConnection(GetConnectionString(_options.DataConnectionStringName));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {tableName}
            WHERE ID = @Id
              AND CREATED_BY = @CreatedBy
              AND PLAN_DATE >= CONVERT(date, GETDATE())
            """;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = planId;
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 256).Value = NormalizeActor(actorUserName);

        var deletedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deletedRows == 0)
        {
            throw new InvalidOperationException("Galima trinti tik savo ateities planus.");
        }
    }

    public IReadOnlyList<AtlasHoliday> GetPublicHolidays(int year)
    {
        var easter = CalculateEasterSunday(year);
        return
        [
            new(new DateTime(year, 1, 1), "Naujieji metai", "New Year's Day"),
            new(new DateTime(year, 2, 16), "Lietuvos valstybes atkurimo diena", "Day of Restoration of the State of Lithuania"),
            new(new DateTime(year, 3, 11), "Lietuvos nepriklausomybes atkurimo diena", "Day of Restoration of Independence of Lithuania"),
            new(easter, "Velykos", "Easter Sunday"),
            new(easter.AddDays(1), "Antroji Velyku diena", "Easter Monday"),
            new(new DateTime(year, 5, 1), "Tarptautine darbo diena", "Labour Day"),
            new(FirstSunday(year, 5), "Motinos diena", "Mother's Day"),
            new(FirstSunday(year, 6), "Tevo diena", "Father's Day"),
            new(new DateTime(year, 6, 24), "Jonines", "St. John's Day"),
            new(new DateTime(year, 7, 6), "Valstybes diena", "Statehood Day"),
            new(new DateTime(year, 8, 15), "Zoline", "Assumption Day"),
            new(new DateTime(year, 11, 1), "Visu sventuju diena", "All Saints' Day"),
            new(new DateTime(year, 11, 2), "Velines", "All Souls' Day"),
            new(new DateTime(year, 12, 24), "Kucios", "Christmas Eve"),
            new(new DateTime(year, 12, 25), "Kaledos", "Christmas Day"),
            new(new DateTime(year, 12, 26), "Antroji Kaledu diena", "Second Day of Christmas")
        ];
    }

    private static AtlasCreatePlan NormalizeCreatePlan(AtlasCreatePlan plan)
    {
        var planType = (plan.PlanType ?? string.Empty).Trim().ToUpperInvariant();
        if (planType is not ("LEAVE" or "WORK_FROM_HOME" or "MEETING"))
        {
            throw new InvalidOperationException("Netinkamas ATLAS plano tipas.");
        }

        return new AtlasCreatePlan(
            plan.StartDate.Date,
            plan.EndDate.Date,
            planType,
            (plan.Title ?? string.Empty).Trim(),
            (plan.Notes ?? string.Empty).Trim());
    }

    private static IEnumerable<DateTime> EnumeratePlanDates(DateTime startDate, DateTime endDate)
    {
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static string NormalizeColor(string color)
    {
        var normalized = (color ?? string.Empty).Trim();
        if (normalized.Length != 7 || normalized[0] != '#')
        {
            return DefaultUserColor;
        }

        return normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized
            : DefaultUserColor;
    }

    private static bool CanDeletePlan(DateTime planDate, string createdBy, string currentUserName)
    {
        return planDate.Date >= DateTime.Today &&
               NormalizeActor(createdBy).Equals(NormalizeActor(currentUserName), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeActor(string actor)
    {
        return actor.Trim();
    }

    private static DateTime CalculateEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }

    private static DateTime FirstSunday(int year, int month)
    {
        var date = new DateTime(year, month, 1);
        while (date.DayOfWeek != DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private string NormalizePlanTableName()
    {
        return SqlName.NormalizeTableName(_options.PlanTableName, DefaultPlanTableName);
    }

    private string NormalizeAccessTableName()
    {
        return SqlName.NormalizeTableName(_options.AccessTableName, DefaultAccessTableName);
    }

    private string NormalizeUserSettingsTableName()
    {
        return SqlName.NormalizeTableName(_options.UserSettingsTableName, DefaultUserSettingsTableName);
    }

    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }
}
