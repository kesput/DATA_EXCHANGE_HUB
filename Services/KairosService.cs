using System.Data;
using System.Globalization;
using System.Text;
using System.Security.Claims;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class KairosService(
    IConfiguration configuration,
    IOptions<KairosOptions> options,
    IUserNameResolver userNameResolver,
    IDataExchangeTableService tableService) : IKairosService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private const string DefaultRequestTableName = "BLS_LOGIT_STAT.dbo.KAIROS_REQUESTS";
    private const string DefaultLogTableName = "BLS_LOGIT_STAT.dbo.KAIROS_REQUEST_LOG";
    private const string DefaultPromoForecastTableName = "BLS_LOGIT_STAT.dbo.KAIROS_PROMO_FORECAST";
    private const string DefaultRequestType = "SPEC_WORK";
    private const string DefaultStatus = "NEW";
    private static readonly TimeSpan CutoffTime = new(11, 0, 0);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly KairosOptions _options = options.Value;
    private ExchangeTableAccess PromoForecastTableAccess => new(
        NormalizePromoForecastTableName(),
        _options.AppName,
        ExchangePermission.UploadAndReceive);

    private sealed record KairosRowSnapshot(
        int Id,
        string RequestType,
        string Title,
        string InfPrek,
        decimal QuantityRequest,
        DateTime NeedDate,
        DateTime MinExpirationRequest,
        string NetworkClient,
        string Comment,
        string Status,
        DateTimeOffset CreatedAt,
        string CreatedBy,
        DateTimeOffset UpdateTime,
        string UpdatedBy);

    private sealed record KairosLogDetails(
        long LogId,
        string ActionType,
        int? RequestId,
        string? RowDataBefore,
        bool IsReverted);

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

    public DateTime GetMinimumNeedDate()
    {
        var now = DateTime.Now;
        return now.TimeOfDay < CutoffTime
            ? DateTime.Today
            : DateTime.Today.AddDays(1);
    }

    public bool IsLocked(DateTime needDate)
    {
        return needDate.Date < GetMinimumNeedDate().Date;
    }

    public async Task<IReadOnlyList<KairosRequest>> ListRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var tableName = NormalizeRequestTableName();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Limit)
                ID,
                REQUEST_TYPE,
                TITLE,
                INF_PREK,
                QUANTITY_REQUEST,
                NEED_DATE,
                MIN_EXPIRATION_REQUEST,
                NETWORK_CLIENT,
                COMMENT,
                STATUS,
                CREATED_AT,
                CREATED_BY,
                UPDATE_TIME,
                UPDATED_BY
            FROM {tableName}
            ORDER BY CREATED_AT DESC, ID DESC
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = GetListLimit();

        var rows = new List<KairosRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ToRequest(ReadSnapshot(reader)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<KairosLogEntry>> ListLogsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var logTableName = NormalizeLogTableName();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Limit)
                LOG_ID,
                EVENT_AT,
                ACTION_TYPE,
                REQUEST_ID,
                ACTOR_USER,
                TITLE,
                NEED_DATE,
                IS_REVERTED
            FROM {logTableName}
            ORDER BY EVENT_AT DESC, LOG_ID DESC
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = GetLogLimit();

        var logs = new List<KairosLogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var needDate = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);
            logs.Add(new KairosLogEntry(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                needDate,
                reader.GetBoolean(7),
                needDate.HasValue && IsLocked(needDate.Value)));
        }

        return logs;
    }

    public async Task<int> CreateRequestAsync(
        KairosCreateRequest request,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCreateRequest(request, actorUserName);
        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var tableName = NormalizeRequestTableName();
        var now = DateTimeOffset.Now;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName}
            (
                REQUEST_TYPE,
                TITLE,
                INF_PREK,
                QUANTITY_REQUEST,
                NEED_DATE,
                MIN_EXPIRATION_REQUEST,
                NETWORK_CLIENT,
                COMMENT,
                STATUS,
                CREATED_AT,
                CREATED_BY,
                UPDATE_TIME,
                UPDATED_BY
            )
            OUTPUT INSERTED.ID
            VALUES
            (
                @RequestType,
                @Title,
                @InfPrek,
                @QuantityRequest,
                @NeedDate,
                @MinExpirationRequest,
                @NetworkClient,
                @Comment,
                @Status,
                @CreatedAt,
                @CreatedBy,
                @UpdateTime,
                @UpdatedBy
            )
            """;
        AddSnapshotParameters(command, normalized, now);

        var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        var snapshot = await LoadSnapshotAsync(connection, transaction, newId, cancellationToken);
        await InsertLogAsync(
            connection,
            transaction,
            "INSERT",
            newId,
            actorUserName,
            rowDataBefore: null,
            rowDataAfter: snapshot,
            cancellationToken);

        transaction.Commit();
        return newId;
    }

    public async Task DeleteRequestAsync(
        int requestId,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var tableName = NormalizeRequestTableName();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var snapshot = await LoadSnapshotAsync(connection, transaction, requestId, cancellationToken);
        if (IsLocked(snapshot.NeedDate))
        {
            throw new InvalidOperationException("Irasas uzrakintas pagal 11:00 taisykle, todel jo trinti nebegalima.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {tableName} WHERE ID = @Id";
        command.Parameters.Add("@Id", SqlDbType.Int).Value = requestId;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await InsertLogAsync(
            connection,
            transaction,
            "DELETE",
            requestId,
            actorUserName,
            rowDataBefore: snapshot,
            rowDataAfter: null,
            cancellationToken);

        transaction.Commit();
    }

    public async Task RestoreDeletedRequestAsync(
        long logId,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var log = await LoadLogDetailsAsync(connection, transaction, logId, cancellationToken);
        if (!log.ActionType.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Atstatyti galima tik istrinimo audit irasa.");
        }

        if (log.IsReverted)
        {
            throw new InvalidOperationException("Sis audit irasas jau atstatytas.");
        }

        if (string.IsNullOrWhiteSpace(log.RowDataBefore))
        {
            throw new InvalidOperationException("Audit irase nera istrinto iraso duomenu.");
        }

        var previous = JsonSerializer.Deserialize<KairosRowSnapshot>(log.RowDataBefore, JsonOptions)
            ?? throw new InvalidOperationException("Nepavyko perskaityti audit iraso duomenu.");

        if (IsLocked(previous.NeedDate))
        {
            throw new InvalidOperationException("Irasas uzrakintas pagal 11:00 taisykle, todel jo atstatyti nebegalima.");
        }

        var restored = previous with
        {
            Id = 0,
            UpdateTime = DateTimeOffset.Now,
            UpdatedBy = actorUserName
        };
        var newId = await InsertSnapshotAsRestoredAsync(
            connection,
            transaction,
            restored,
            cancellationToken);
        restored = restored with { Id = newId };

        var restoreLogId = await InsertLogAsync(
            connection,
            transaction,
            "RESTORE",
            newId,
            actorUserName,
            rowDataBefore: null,
            rowDataAfter: restored,
            cancellationToken);

        await MarkLogRevertedAsync(
            connection,
            transaction,
            log.LogId,
            restoreLogId,
            actorUserName,
            cancellationToken);

        transaction.Commit();
    }


    public async Task<IReadOnlyList<KairosPromoForecast>> ListPromoForecastAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var tableName = NormalizePromoForecastTableName();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Limit)
                ID,
                CLIENT,
                INF_PREK,
                BUY_START,
                PROMO_START,
                PROMO_END,
                QTY,
                INFO_DATE,
                STATUS,
                DAILY_SALES,
                KEY_VALUE,
                UPLOADED_AT,
                UPLOADED_BY,
                UPDATE_TIME,
                UPDATED_BY
            FROM {tableName}
            ORDER BY UPDATE_TIME DESC, ID DESC
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = GetListLimit();

        var rows = new List<KairosPromoForecast>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new KairosPromoForecast(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                reader.GetDateTime(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetString(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetString(14)));
        }

        return rows;
    }

    public async Task DeletePromoForecastAsync(
        int id,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new InvalidOperationException("PROMO FORECAST iraso ID neteisingas.");
        }

        if (string.IsNullOrWhiteSpace(actorUserName))
        {
            throw new InvalidOperationException("Nepavyko nustatyti prisijungusio Windows vartotojo.");
        }

        var connectionString = GetConnectionString(_options.DataConnectionStringName);
        var tableName = NormalizePromoForecastTableName();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {tableName} WHERE ID = @Id";
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        var deletedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deletedRows == 0)
        {
            throw new InvalidOperationException("PROMO FORECAST irasas nerastas.");
        }
    }
    public async Task<KairosPromoForecastImportResult> ImportPromoForecastAsync(
        ExcelWorksheetData worksheet,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (worksheet.Headers.Count == 0 || worksheet.Rows.Count == 0)
        {
            throw new InvalidOperationException("Excel faile nera PROMO FORECAST duomenu.");
        }

        var headerMap = BuildPromoForecastHeaderMap(worksheet.Headers);
        var requiredFields = new[] { "CLIENT", "INF_PREK", "PROMO_START", "PROMO_END" };
        var missingFields = requiredFields
            .Where(field => !headerMap.Values.Contains(field, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingFields.Length > 0)
        {
            throw new InvalidOperationException($"Excel faile truksta privalomu stulpeliu: {string.Join(", ", missingFields)}.");
        }

        var ignoredHeaders = worksheet.Headers
            .Where(header => !headerMap.ContainsKey(header))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = worksheet.Rows
            .Select((row, index) => BuildPromoForecastRow(row, headerMap, index + 2, actorUserName))
            .Select(ToPromoForecastExchangeRow)
            .ToArray();

        if (rows.Length == 0)
        {
            throw new InvalidOperationException("Excel faile nera importuojamu PROMO FORECAST eiluciu.");
        }

        var savedRows = await tableService.AddRowsAsync(
            PromoForecastTableAccess,
            rows,
            actorUserName,
            cancellationToken);

        return new KairosPromoForecastImportResult(savedRows, 0, ignoredHeaders.Length, ignoredHeaders);
    }

    private static IReadOnlyDictionary<string, string?> ToPromoForecastExchangeRow(KairosPromoForecast row)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLIENT"] = row.Client,
            ["INF_PREK"] = row.InfPrek,
            ["BUY_START"] = FormatImportDate(row.BuyStart),
            ["PROMO_START"] = FormatImportDate(row.PromoStart),
            ["PROMO_END"] = FormatImportDate(row.PromoEnd),
            ["QTY"] = FormatImportDecimal(row.Qty),
            ["INFO_DATE"] = FormatImportDate(row.InfoDate),
            ["STATUS"] = row.Status,
            ["DAILY_SALES"] = FormatImportDecimal(row.DailySales),
            ["KEY_VALUE"] = row.KeyValue
        };

        return values;
    }

    private static string? FormatImportDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? FormatImportDecimal(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : null;
    }
    private static IReadOnlyDictionary<string, string> BuildPromoForecastHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var normalized = NormalizePromoForecastHeader(header);
            var field = normalized switch
            {
                "klientas" => "CLIENT",
                "vidiniskodas" => "INF_PREK",
                "klientaspradedapirkti" => "BUY_START",
                "akcijospradziosdata" => "PROMO_START",
                "akcijospabaigosdata" => "PROMO_END",
                "numatomaspardu 異kiekis" => "QTY",
                "numatomasparduotikiekis" => "QTY",
                "infogavimodata" => "INFO_DATE",
                "status" => "STATUS",
                "dienosapyvartapriesakcija" => "DAILY_SALES",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(field))
            {
                map[header] = field;
            }
        }

        return map;
    }

    private static KairosPromoForecast BuildPromoForecastRow(
        IReadOnlyDictionary<string, string?> source,
        IReadOnlyDictionary<string, string> headerMap,
        int excelRowNumber,
        string actorUserName)
    {
        var client = GetPromoValue(source, headerMap, "CLIENT").Trim();
        var infPrek = GetPromoValue(source, headerMap, "INF_PREK").Trim();
        var promoStart = ParsePromoDate(GetPromoValue(source, headerMap, "PROMO_START"), "Akcijos pradzios data", excelRowNumber);
        var promoEnd = ParsePromoDate(GetPromoValue(source, headerMap, "PROMO_END"), "Akcijos pabaigos data", excelRowNumber);

        if (string.IsNullOrWhiteSpace(client))
        {
            throw new InvalidOperationException($"Excel eilute {excelRowNumber}: Klientas yra privalomas.");
        }

        if (string.IsNullOrWhiteSpace(infPrek) || infPrek.Length != 7)
        {
            throw new InvalidOperationException($"Excel eilute {excelRowNumber}: Vidinis kodas / INF_PREK turi buti tiksliai 7 simboliai.");
        }

        if (promoEnd < promoStart)
        {
            throw new InvalidOperationException($"Excel eilute {excelRowNumber}: akcijos pabaiga negali buti ankstesne uz pradzia.");
        }

        var now = DateTimeOffset.Now;
        var keyValue = BuildPromoForecastKey(client, infPrek, promoStart, promoEnd);
        return new KairosPromoForecast(
            0,
            client,
            infPrek,
            ParseOptionalPromoDate(GetPromoValue(source, headerMap, "BUY_START"), "Klientas pradeda pirkti", excelRowNumber),
            promoStart,
            promoEnd,
            ParseOptionalPromoDecimal(GetPromoValue(source, headerMap, "QTY"), "Numatomas parduoti kiekis", excelRowNumber),
            ParseOptionalPromoDate(GetPromoValue(source, headerMap, "INFO_DATE"), "Info gavimo data", excelRowNumber),
            NullIfWhiteSpace(GetPromoValue(source, headerMap, "STATUS")),
            ParseOptionalPromoDecimal(GetPromoValue(source, headerMap, "DAILY_SALES"), "Dienos apyvarta pries akcija", excelRowNumber),
            keyValue,
            now,
            actorUserName,
            now,
            actorUserName);
    }

    private static void AddPromoForecastParameters(SqlCommand command, KairosPromoForecast row)
    {
        command.Parameters.Add("@Client", SqlDbType.NVarChar, 100).Value = row.Client;
        command.Parameters.Add("@InfPrek", SqlDbType.NVarChar, 7).Value = row.InfPrek;
        command.Parameters.Add("@BuyStart", SqlDbType.Date).Value = row.BuyStart.HasValue ? row.BuyStart.Value.Date : DBNull.Value;
        command.Parameters.Add("@PromoStart", SqlDbType.Date).Value = row.PromoStart.Date;
        command.Parameters.Add("@PromoEnd", SqlDbType.Date).Value = row.PromoEnd.Date;
        command.Parameters.Add("@Qty", SqlDbType.Decimal).Value = row.Qty.HasValue ? row.Qty.Value : DBNull.Value;
        command.Parameters["@Qty"].Precision = 18;
        command.Parameters["@Qty"].Scale = 3;
        command.Parameters.Add("@InfoDate", SqlDbType.Date).Value = row.InfoDate.HasValue ? row.InfoDate.Value.Date : DBNull.Value;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(row.Status) ? DBNull.Value : row.Status;
        command.Parameters.Add("@DailySales", SqlDbType.Decimal).Value = row.DailySales.HasValue ? row.DailySales.Value : DBNull.Value;
        command.Parameters["@DailySales"].Precision = 18;
        command.Parameters["@DailySales"].Scale = 3;
        command.Parameters.Add("@KeyValue", SqlDbType.NVarChar, 300).Value = row.KeyValue;
        command.Parameters.Add("@UploadedAt", SqlDbType.DateTimeOffset).Value = row.UploadedAt;
        command.Parameters.Add("@UploadedBy", SqlDbType.NVarChar, 256).Value = row.UploadedBy;
        command.Parameters.Add("@UpdateTime", SqlDbType.DateTimeOffset).Value = row.UpdateTime;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = row.UpdatedBy;
    }

    private static string GetPromoValue(
        IReadOnlyDictionary<string, string?> source,
        IReadOnlyDictionary<string, string> headerMap,
        string fieldName)
    {
        foreach (var (header, field) in headerMap)
        {
            if (field.Equals(fieldName, StringComparison.OrdinalIgnoreCase) &&
                source.TryGetValue(header, out var value))
            {
                return value ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string NormalizePromoForecastHeader(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static DateTime ParsePromoDate(string value, string fieldName, int excelRowNumber)
    {
        return ParseOptionalPromoDate(value, fieldName, excelRowNumber)
            ?? throw new InvalidOperationException($"Excel eilute {excelRowNumber}: {fieldName} yra privaloma data.");
    }

    private static DateTime? ParseOptionalPromoDate(string value, string fieldName, int excelRowNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("lt-LT"), DateTimeStyles.None, out var date) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date.Date;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("lt-LT"), out var serial) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out serial))
        {
            try
            {
                return DateTime.FromOADate((double)serial).Date;
            }
            catch (ArgumentException)
            {
            }
        }

        throw new InvalidOperationException($"Excel eilute {excelRowNumber}: {fieldName} nera teisinga data.");
    }

    private static decimal? ParseOptionalPromoDecimal(string value, string fieldName, int excelRowNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("lt-LT"), out var number) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidOperationException($"Excel eilute {excelRowNumber}: {fieldName} nera teisingas skaicius.");
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildPromoForecastKey(string client, string infPrek, DateTime promoStart, DateTime promoEnd)
    {
        return string.Join("|", client.Trim().ToUpperInvariant(), infPrek.Trim().ToUpperInvariant(), promoStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture), promoEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
    }

    private KairosRowSnapshot NormalizeCreateRequest(
        KairosCreateRequest request,
        string actorUserName)
    {
        var title = request.Title.Trim();
        var infPrek = request.InfPrek.Trim();
        var networkClient = request.NetworkClient.Trim();
        var comment = request.Comment.Trim();
        var needDate = request.NeedDate.Date;
        var minExpirationRequest = request.MinExpirationRequest.Date;

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Iveskite darbo pavadinima.");
        }

        if (string.IsNullOrWhiteSpace(infPrek) || infPrek.Length != 7)
        {
            throw new InvalidOperationException("INF_PREK turi buti tiksliai 7 simboliai.");
        }

        if (request.QuantityRequest <= 0)
        {
            throw new InvalidOperationException("Kiekis turi buti didesnis uz 0.");
        }

        if (needDate < GetMinimumNeedDate().Date)
        {
            throw new InvalidOperationException($"Poreikio data negali buti ankstesne nei {GetMinimumNeedDate():yyyy-MM-dd}.");
        }

        if (minExpirationRequest < needDate)
        {
            throw new InvalidOperationException("Minimalus galiojimas negali buti ankstesnis uz poreikio data.");
        }

        if (string.IsNullOrWhiteSpace(networkClient))
        {
            throw new InvalidOperationException("Iveskite tinkla arba klienta, kuriam atliekamas darbas.");
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new InvalidOperationException("Iveskite paaiskinima, kam reikalingas specialus darbas.");
        }

        if (string.IsNullOrWhiteSpace(actorUserName))
        {
            throw new InvalidOperationException("Nepavyko nustatyti prisijungusio Windows vartotojo.");
        }

        return new KairosRowSnapshot(
            0,
            DefaultRequestType,
            title,
            infPrek,
            request.QuantityRequest,
            needDate,
            minExpirationRequest,
            networkClient,
            comment,
            DefaultStatus,
            DateTimeOffset.Now,
            actorUserName,
            DateTimeOffset.Now,
            actorUserName);
    }

    private async Task<KairosRowSnapshot> LoadSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int requestId,
        CancellationToken cancellationToken)
    {
        var tableName = NormalizeRequestTableName();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                REQUEST_TYPE,
                TITLE,
                INF_PREK,
                QUANTITY_REQUEST,
                NEED_DATE,
                MIN_EXPIRATION_REQUEST,
                NETWORK_CLIENT,
                COMMENT,
                STATUS,
                CREATED_AT,
                CREATED_BY,
                UPDATE_TIME,
                UPDATED_BY
            FROM {tableName}
            WHERE ID = @Id
            """;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = requestId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("KAIROS irasas nerastas.");
        }

        return ReadSnapshot(reader);
    }

    private async Task<KairosLogDetails> LoadLogDetailsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long logId,
        CancellationToken cancellationToken)
    {
        var logTableName = NormalizeLogTableName();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                LOG_ID,
                ACTION_TYPE,
                REQUEST_ID,
                ROW_DATA_BEFORE,
                IS_REVERTED
            FROM {logTableName}
            WHERE LOG_ID = @LogId
            """;
        command.Parameters.Add("@LogId", SqlDbType.BigInt).Value = logId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("KAIROS audit irasas nerastas.");
        }

        return new KairosLogDetails(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4));
    }

    private async Task<int> InsertSnapshotAsRestoredAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        KairosRowSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var tableName = NormalizeRequestTableName();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName}
            (
                REQUEST_TYPE,
                TITLE,
                INF_PREK,
                QUANTITY_REQUEST,
                NEED_DATE,
                MIN_EXPIRATION_REQUEST,
                NETWORK_CLIENT,
                COMMENT,
                STATUS,
                CREATED_AT,
                CREATED_BY,
                UPDATE_TIME,
                UPDATED_BY
            )
            OUTPUT INSERTED.ID
            VALUES
            (
                @RequestType,
                @Title,
                @InfPrek,
                @QuantityRequest,
                @NeedDate,
                @MinExpirationRequest,
                @NetworkClient,
                @Comment,
                @Status,
                @CreatedAt,
                @CreatedBy,
                @UpdateTime,
                @UpdatedBy
            )
            """;
        AddSnapshotParameters(command, snapshot, snapshot.UpdateTime);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<long> InsertLogAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string actionType,
        int? requestId,
        string actorUserName,
        KairosRowSnapshot? rowDataBefore,
        KairosRowSnapshot? rowDataAfter,
        CancellationToken cancellationToken)
    {
        var logTableName = NormalizeLogTableName();
        var rowForMetadata = rowDataAfter ?? rowDataBefore;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {logTableName}
            (
                ACTION_TYPE,
                REQUEST_ID,
                ACTOR_USER,
                TITLE,
                NEED_DATE,
                ROW_DATA_BEFORE,
                ROW_DATA_AFTER
            )
            OUTPUT INSERTED.LOG_ID
            VALUES
            (
                @ActionType,
                @RequestId,
                @ActorUser,
                @Title,
                @NeedDate,
                @RowDataBefore,
                @RowDataAfter
            )
            """;
        command.Parameters.Add("@ActionType", SqlDbType.NVarChar, 30).Value = actionType;
        command.Parameters.Add("@RequestId", SqlDbType.Int).Value =
            requestId.HasValue ? (object)requestId.Value : DBNull.Value;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = actorUserName;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value =
            rowForMetadata is null ? DBNull.Value : (object)rowForMetadata.Title;
        command.Parameters.Add("@NeedDate", SqlDbType.Date).Value =
            rowForMetadata is null ? DBNull.Value : (object)rowForMetadata.NeedDate.Date;
        command.Parameters.Add("@RowDataBefore", SqlDbType.NVarChar, -1).Value =
            rowDataBefore is null ? DBNull.Value : (object)JsonSerializer.Serialize(rowDataBefore, JsonOptions);
        command.Parameters.Add("@RowDataAfter", SqlDbType.NVarChar, -1).Value =
            rowDataAfter is null ? DBNull.Value : (object)JsonSerializer.Serialize(rowDataAfter, JsonOptions);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task MarkLogRevertedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long logId,
        long restoreLogId,
        string actorUserName,
        CancellationToken cancellationToken)
    {
        var logTableName = NormalizeLogTableName();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {logTableName}
            SET IS_REVERTED = 1,
                REVERTED_AT = @RevertedAt,
                REVERTED_BY = @RevertedBy,
                RESTORE_LOG_ID = @RestoreLogId
            WHERE LOG_ID = @LogId
            """;
        command.Parameters.Add("@RevertedAt", SqlDbType.DateTimeOffset).Value = DateTimeOffset.Now;
        command.Parameters.Add("@RevertedBy", SqlDbType.NVarChar, 256).Value = actorUserName;
        command.Parameters.Add("@RestoreLogId", SqlDbType.BigInt).Value = restoreLogId;
        command.Parameters.Add("@LogId", SqlDbType.BigInt).Value = logId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddSnapshotParameters(
        SqlCommand command,
        KairosRowSnapshot snapshot,
        DateTimeOffset updateTime)
    {
        command.Parameters.Add("@RequestType", SqlDbType.NVarChar, 50).Value = snapshot.RequestType;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = snapshot.Title;
        command.Parameters.Add("@InfPrek", SqlDbType.NVarChar, 7).Value = snapshot.InfPrek;
        command.Parameters.Add("@QuantityRequest", SqlDbType.Decimal).Value = snapshot.QuantityRequest;
        command.Parameters["@QuantityRequest"].Precision = 18;
        command.Parameters["@QuantityRequest"].Scale = 3;
        command.Parameters.Add("@NeedDate", SqlDbType.Date).Value = snapshot.NeedDate.Date;
        command.Parameters.Add("@MinExpirationRequest", SqlDbType.Date).Value = snapshot.MinExpirationRequest.Date;
        command.Parameters.Add("@NetworkClient", SqlDbType.NVarChar, 200).Value = snapshot.NetworkClient;
        command.Parameters.Add("@Comment", SqlDbType.NVarChar, 1000).Value = snapshot.Comment;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = snapshot.Status;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTimeOffset).Value = snapshot.CreatedAt;
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 256).Value = snapshot.CreatedBy;
        command.Parameters.Add("@UpdateTime", SqlDbType.DateTimeOffset).Value = updateTime;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = snapshot.UpdatedBy;
    }

    private KairosRequest ToRequest(KairosRowSnapshot snapshot)
    {
        return new KairosRequest(
            snapshot.Id,
            snapshot.RequestType,
            snapshot.Title,
            snapshot.InfPrek,
            snapshot.QuantityRequest,
            snapshot.NeedDate,
            snapshot.MinExpirationRequest,
            snapshot.NetworkClient,
            snapshot.Comment,
            snapshot.Status,
            snapshot.CreatedAt,
            snapshot.CreatedBy,
            snapshot.UpdateTime,
            snapshot.UpdatedBy,
            IsLocked(snapshot.NeedDate));
    }

    private static KairosRowSnapshot ReadSnapshot(SqlDataReader reader)
    {
        return new KairosRowSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetDateTime(5),
            reader.GetDateTime(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.GetString(13));
    }

    private int GetListLimit()
    {
        return Math.Clamp(_options.ListLimit, 1, 1000);
    }

    private int GetLogLimit()
    {
        return Math.Clamp(_options.LogLimit, 1, 200);
    }

    private string NormalizeRequestTableName()
    {
        return SqlName.NormalizeTableName(_options.RequestTableName, DefaultRequestTableName);
    }

    private string NormalizeLogTableName()
    {
        return SqlName.NormalizeTableName(_options.LogTableName, DefaultLogTableName);
    }

    private string NormalizePromoForecastTableName()
    {
        return SqlName.NormalizeTableName(_options.PromoForecastTableName, DefaultPromoForecastTableName);
    }
    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }
}







