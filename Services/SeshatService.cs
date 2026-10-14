using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using DATA_EXCHANGE_HUB.Pages.Hub;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class SeshatService(
    IConfiguration configuration,
    IOptions<SeshatOptions> options,
    IOptions<FileUploadOptions> fileUploadOptions,
    ISendMailService sendMailService,
    ISendSmsService sendSmsService,
    IUserNameResolver userNameResolver,
    IHttpContextAccessor httpContextAccessor,
    IWebHostEnvironment environment) : ISeshatService
{
    private const string DefaultDocumentsTableName = "BLS_LOGIT_STAT.dbo.SESHAT_DOCUMENTS";
    private const string DefaultDocumentCountriesTableName = "BLS_LOGIT_STAT.dbo.SESHAT_DOCUMENT_COUNTRIES";
    private const string DefaultFilesTableName = "BLS_LOGIT_STAT.dbo.SESHAT_FILES";
    private const string DefaultSectionsTableName = "BLS_LOGIT_STAT.dbo.SESHAT_SECTIONS";
    private const string DefaultRemindersTableName = "BLS_LOGIT_STAT.dbo.SESHAT_REMINDERS";
    private const string DefaultLogTableName = "BLS_LOGIT_STAT.dbo.SESHAT_LOG";
    private const string DefaultAccessTableName = "BLS_LOGIT_STAT.dbo.BS_APP_ACCESS";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SeshatOptions _options = options.Value;
    private readonly FileUploadOptions _fileUploadOptions = fileUploadOptions.Value;

    public async Task<SeshatAccessState> GetAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = ResolveSeshatUserName(user);
        if (IsDevelopmentPasswordSession())
        {
            return new SeshatAccessState(userName, true, new HashSet<string>(), new HashSet<string>());
        }

        var viewCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contributeCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isAdmin = false;

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        var activePredicate = _options.UseAccessActiveColumn
            ? $"AND (NULLIF(LTRIM(RTRIM({SqlColumn(_options.AccessActiveColumn)})), N'') IS NULL OR UPPER(LTRIM(RTRIM({SqlColumn(_options.AccessActiveColumn)}))) IN (N'1', N'TRUE', N'Y', N'YES', N'ACTIVE'))"
            : string.Empty;
        var dimensionPredicate = _options.UseAccessDimensionColumn
            ? $"AND (UPPER({SqlColumn(_options.AccessLevelColumn)}) = N'ADMIN' OR UPPER({SqlColumn(_options.AccessDimensionColumn)}) = UPPER(@CountryDimension))"
            : string.Empty;
        command.CommandText = $"""
            SELECT
                {SqlColumn(_options.AccessDimensionValueColumn)} AS COUNTRY_CODE,
                {SqlColumn(_options.AccessLevelColumn)} AS ACCESS_LEVEL
            FROM {AccessTableName()}
            WHERE {SqlColumn(_options.AccessAppNameColumn)} = @AppName
              AND UPPER({SqlColumn(_options.AccessUserColumn)}) = UPPER(@UserName)
              {activePredicate}
              {dimensionPredicate}
            """;
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = _options.AppName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;
        if (_options.UseAccessDimensionColumn)
        {
            command.Parameters.Add("@CountryDimension", SqlDbType.NVarChar, 50).Value = _options.CountryDimensionName;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var country = reader.IsDBNull(0) ? string.Empty : NormalizeCountry(reader.GetString(0));
            var level = reader.GetString(1).Trim().ToUpperInvariant();
            if (level == "ADMIN")
            {
                isAdmin = true;
                continue;
            }

            if (level == "CONTRIBUTE")
            {
                contributeCountries.Add(country);
                viewCountries.Add(country);
            }
            else if (level == "VIEW")
            {
                viewCountries.Add(country);
            }
        }

        return new SeshatAccessState(userName, isAdmin, viewCountries, contributeCountries);
    }

    public async Task<IReadOnlyList<SeshatDocumentSummary>> ListDocumentsAsync(
        SeshatAccessState access,
        string? search,
        string? status,
        string? country,
        CancellationToken cancellationToken = default)
    {
        if (!access.CanOpen)
        {
            return [];
        }

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Limit)
                d.ID,
                d.TITLE,
                d.DESCRIPTION,
                d.KEYWORDS,
                d.STATUS,
                d.VALID_FROM,
                d.VALID_TO,
                d.CREATED_BY,
                d.CREATED_AT,
                d.UPDATED_BY,
                d.UPDATE_TIME,
                d.REMINDER_DAYS_BEFORE,
                COALESCE(c.COUNTRIES, N'') AS COUNTRIES,
                (
                    SELECT COUNT(1)
                    FROM {FilesTableName()} f
                    WHERE f.DOCUMENT_ID = d.ID
                      AND f.IS_DELETED = 0
                ) AS FILE_COUNT
            FROM {DocumentsTableName()} d
            OUTER APPLY (
                SELECT STUFF((
                    SELECT N',' + dc2.COUNTRY_CODE
                    FROM {DocumentCountriesTableName()} dc2
                    WHERE dc2.DOCUMENT_ID = d.ID
                    ORDER BY dc2.COUNTRY_CODE
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 1, N'') AS COUNTRIES
            ) c
            WHERE (@Status = N'' OR d.STATUS = @Status)
              AND (@Country = N'' OR EXISTS (
                    SELECT 1
                    FROM {DocumentCountriesTableName()} fc
                    WHERE fc.DOCUMENT_ID = d.ID
                      AND fc.COUNTRY_CODE = @Country
              ))
              AND (
                    @Search = N''
                    OR d.TITLE LIKE @SearchLike
                    OR d.DESCRIPTION LIKE @SearchLike
                    OR d.KEYWORDS LIKE @SearchLike
                    OR d.CREATED_BY LIKE @SearchLike
                    OR EXISTS (
                        SELECT 1
                        FROM {FilesTableName()} sf
                        WHERE sf.DOCUMENT_ID = d.ID
                          AND sf.IS_DELETED = 0
                          AND (sf.ORIGINAL_FILE_NAME LIKE @SearchLike OR sf.DESCRIPTION LIKE @SearchLike)
                    )
              )
              AND EXISTS (
                    SELECT 1
                    FROM {DocumentCountriesTableName()} ac
                    WHERE ac.DOCUMENT_ID = d.ID
                      AND ({BuildAccessPredicate(access, command, "ac.COUNTRY_CODE", requireContribute: false)})
              )
            ORDER BY d.UPDATE_TIME DESC, d.ID DESC
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(_options.ListLimit, 1, 500);
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = NormalizeOptional(status);
        command.Parameters.Add("@Country", SqlDbType.NVarChar, 10).Value = NormalizeOptional(country).ToUpperInvariant();
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = NormalizeOptional(search);
        command.Parameters.Add("@SearchLike", SqlDbType.NVarChar, 220).Value = $"%{NormalizeOptional(search)}%";

        var rows = new List<SeshatDocumentSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadDocumentSummary(reader));
        }

        return rows;
    }

    public async Task<SeshatDocumentDetails?> GetDocumentDetailsAsync(
        int documentId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        if (!access.CanOpen)
        {
            return null;
        }

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var files = await LoadFilesAsync(connection, documentId, includeDeleted: true, cancellationToken);
        var sections = await LoadSectionsAsync(connection, documentId, files, cancellationToken);
        var logs = await LoadLogsAsync(connection, documentId, cancellationToken);
        var reminders = await LoadRemindersAsync(connection, documentId, cancellationToken);
        return new SeshatDocumentDetails(document, files, sections, reminders, logs);
    }

    public async Task<int> SaveDocumentAsync(
        int? documentId,
        string title,
        string description,
        string keywords,
        string status,
        DateTime? validFrom,
        DateTime? validTo,
        string countriesText,
        int? reminderDaysBefore,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        var countries = ParseCountries(countriesText);
        var normalizedTitle = NormalizeRequired(title, "Iveskite SESHAT pavadinima.", 200);
        var normalizedDescription = NormalizeRequired(description, "Iveskite susitarimo / dokumentacijos aprasyma.", 4000);
        var normalizedKeywords = NormalizeOptional(keywords, 500);
        var normalizedStatus = NormalizeStatus(status);

        if (countries.Count == 0)
        {
            throw new InvalidOperationException("Nurodykite bent viena sali, pvz. LT arba LV.");
        }

        if (!access.CanContributeAny(countries))
        {
            throw new InvalidOperationException("Neturite CONTRIBUTE teises ne vienai nurodytai saliai.");
        }

        if (validFrom.HasValue && validTo.HasValue && validTo.Value.Date < validFrom.Value.Date)
        {
            throw new InvalidOperationException("Galioja iki negali buti ankstesne nei galioja nuo.");
        }

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        SeshatDocumentSummary? before = null;
        if (documentId.HasValue)
        {
            before = await LoadDocumentAsync(connection, documentId.Value, access, cancellationToken, transaction);
            if (before is null)
            {
                throw new InvalidOperationException("SESHAT irasas nerastas arba neturite teises ji keisti.");
            }

            if (!access.CanContributeAny(before.Countries))
            {
                throw new InvalidOperationException("Neturite CONTRIBUTE teises sio dokumento salims.");
            }
        }

        int savedId;
        if (documentId.HasValue)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE {DocumentsTableName()}
                SET TITLE = @Title,
                    DESCRIPTION = @Description,
                    KEYWORDS = @Keywords,
                    STATUS = @Status,
                    VALID_FROM = @ValidFrom,
                    VALID_TO = @ValidTo,
                    REMINDER_DAYS_BEFORE = @ReminderDaysBefore,
                    UPDATED_BY = @UpdatedBy,
                    UPDATE_TIME = SYSDATETIMEOFFSET()
                WHERE ID = @Id
                """;
            AddDocumentParameters(update, normalizedTitle, normalizedDescription, normalizedKeywords, normalizedStatus, validFrom, validTo, reminderDaysBefore, access.UserName);
            update.Parameters.Add("@Id", SqlDbType.Int).Value = documentId.Value;
            await update.ExecuteNonQueryAsync(cancellationToken);
            savedId = documentId.Value;

            await ReplaceCountriesAsync(connection, transaction, savedId, countries, cancellationToken);
            await InsertLogAsync(connection, transaction, savedId, null, "DOCUMENT_UPDATE", access.UserName, "Atnaujintas SESHAT irasas.", before, cancellationToken);
        }
        else
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {DocumentsTableName()}
                (
                    TITLE,
                    DESCRIPTION,
                    KEYWORDS,
                    STATUS,
                    VALID_FROM,
                    VALID_TO,
                    REMINDER_DAYS_BEFORE,
                    CREATED_BY,
                    CREATED_AT,
                    UPDATED_BY,
                    UPDATE_TIME
                )
                OUTPUT INSERTED.ID
                VALUES
                (
                    @Title,
                    @Description,
                    @Keywords,
                    @Status,
                    @ValidFrom,
                    @ValidTo,
                    @ReminderDaysBefore,
                    @UpdatedBy,
                    SYSDATETIMEOFFSET(),
                    @UpdatedBy,
                    SYSDATETIMEOFFSET()
                )
                """;
            AddDocumentParameters(insert, normalizedTitle, normalizedDescription, normalizedKeywords, normalizedStatus, validFrom, validTo, reminderDaysBefore, access.UserName);
            savedId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
            await ReplaceCountriesAsync(connection, transaction, savedId, countries, cancellationToken);
            await InsertLogAsync(connection, transaction, savedId, null, "DOCUMENT_CREATE", access.UserName, "Sukurtas SESHAT irasas.", null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return savedId;
    }

    public async Task UploadFileAsync(
        int documentId,
        IFormFile file,
        string description,
        int? sectionId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Pasirinktas failas tuscias.");
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"Failas per didelis. Maksimalus dydis: {_options.MaxFileSizeMb} MB.");
        }

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises kelti failu prie sio SESHAT iraso.");
        }

        var originalName = BuildSafeFileName(file.FileName);
        var fileKind = NormalizeFileKind(originalName);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        Directory.CreateDirectory(GetActiveFilesDirectory(documentId));

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int fileId;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {FilesTableName()}
                (
                    DOCUMENT_ID,
                    ORIGINAL_FILE_NAME,
                    SECTION_ID,
                    STORED_FILE_NAME,
                    CONTENT_TYPE,
                    FILE_SIZE_BYTES,
                    FILE_KIND,
                    DESCRIPTION,
                    STORAGE_PATH,
                    UPLOADED_BY,
                    UPLOADED_AT
                )
                OUTPUT INSERTED.ID
                VALUES
                (
                    @DocumentId,
                    @OriginalFileName,
                    @SectionId,
                    N'',
                    @ContentType,
                    @FileSizeBytes,
                    @FileKind,
                    @Description,
                    N'',
                    @UploadedBy,
                    SYSDATETIMEOFFSET()
                )
                """;
            insert.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
            insert.Parameters.Add("@OriginalFileName", SqlDbType.NVarChar, 260).Value = originalName;
            insert.Parameters.Add("@SectionId", SqlDbType.Int).Value = sectionId.HasValue ? sectionId.Value : DBNull.Value;
            insert.Parameters.Add("@ContentType", SqlDbType.NVarChar, 200).Value = contentType;
            insert.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt).Value = file.Length;
            insert.Parameters.Add("@FileKind", SqlDbType.NVarChar, 30).Value = fileKind;
            insert.Parameters.Add("@Description", SqlDbType.NVarChar, 1000).Value = NormalizeOptional(description, 1000);
            insert.Parameters.Add("@UploadedBy", SqlDbType.NVarChar, 256).Value = access.UserName;
            fileId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
        }

        var storedName = $"{fileId}_{originalName}";
        var relativePath = Path.Combine("Files", documentId.ToString(), storedName);
        var physicalPath = Path.Combine(GetStorageRoot(), relativePath);
        await using (var stream = File.Create(physicalPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE {FilesTableName()}
                SET STORED_FILE_NAME = @StoredFileName,
                    STORAGE_PATH = @StoragePath
                WHERE ID = @FileId
                """;
            update.Parameters.Add("@StoredFileName", SqlDbType.NVarChar, 320).Value = storedName;
            update.Parameters.Add("@StoragePath", SqlDbType.NVarChar, 1000).Value = relativePath;
            update.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId;
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertLogAsync(connection, transaction, documentId, fileId, "FILE_UPLOAD", access.UserName, $"Prikabintas failas {originalName}.", null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }


    public async Task UploadFilesAsync(
        int documentId,
        IReadOnlyList<IFormFile> files,
        string description,
        int? sectionId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        var selectedFiles = files.Where(file => file.Length > 0).ToArray();
        if (selectedFiles.Length == 0)
        {
            throw new InvalidOperationException("Pasirinkite bent viena faila.");
        }

        foreach (var file in selectedFiles)
        {
            await UploadFileAsync(documentId, file, description, sectionId, access, cancellationToken);
        }
    }
    public async Task<int> CreateSectionAsync(
        int documentId,
        string title,
        string body,
        IReadOnlyList<IFormFile> files,
        string fileDescription,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises kurti punkto prie sio SESHAT iraso.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {SectionsTableName()}
            (
                DOCUMENT_ID,
                TITLE,
                BODY,
                SORT_ORDER,
                CREATED_BY,
                CREATED_AT
            )
            OUTPUT INSERTED.ID
            VALUES
            (
                @DocumentId,
                @Title,
                @Body,
                (SELECT ISNULL(MAX(SORT_ORDER), 0) + 10 FROM {SectionsTableName()} WHERE DOCUMENT_ID = @DocumentId),
                @CreatedBy,
                SYSDATETIMEOFFSET()
            )
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = NormalizeOptional(title, 200);
        command.Parameters.Add("@Body", SqlDbType.NVarChar, -1).Value = NormalizeRequired(body, "Iveskite punkto aprasa.", 4000);
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 256).Value = access.UserName;
        var sectionId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

        await InsertLogWithoutTransactionAsync(connection, documentId, null, "SECTION_CREATE", access.UserName, "Sukurtas papildomas SESHAT punktas.", cancellationToken);

        if (files.Any(file => file.Length > 0))
        {
            await UploadFilesAsync(documentId, files, fileDescription, sectionId, access, cancellationToken);
        }

        return sectionId;
    }

    public async Task UpdateSectionAsync(
        int sectionId,
        string title,
        string body,
        IReadOnlyList<IFormFile> files,
        string fileDescription,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var lookup = connection.CreateCommand();
        lookup.CommandText = $"""
            SELECT DOCUMENT_ID
            FROM {SectionsTableName()}
            WHERE ID = @SectionId
            """;
        lookup.Parameters.Add("@SectionId", SqlDbType.Int).Value = sectionId;
        var documentIdValue = await lookup.ExecuteScalarAsync(cancellationToken);
        if (documentIdValue is null || documentIdValue is DBNull)
        {
            throw new InvalidOperationException("SESHAT punktas nerastas.");
        }

        var documentId = Convert.ToInt32(documentIdValue);
        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises koreguoti punkto prie sio SESHAT iraso.");
        }

        await using var update = connection.CreateCommand();
        update.CommandText = $"""
            UPDATE {SectionsTableName()}
            SET TITLE = @Title,
                BODY = @Body
            WHERE ID = @SectionId
            """;
        update.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = NormalizeOptional(title, 200);
        update.Parameters.Add("@Body", SqlDbType.NVarChar, -1).Value = NormalizeRequired(body, "Iveskite punkto aprasa.", 4000);
        update.Parameters.Add("@SectionId", SqlDbType.Int).Value = sectionId;
        await update.ExecuteNonQueryAsync(cancellationToken);

        await InsertLogWithoutTransactionAsync(connection, documentId, null, "SECTION_UPDATE", access.UserName, "Atnaujintas SESHAT punktas.", cancellationToken);

        if (files.Any(file => file.Length > 0))
        {
            await UploadFilesAsync(documentId, files, fileDescription, sectionId, access, cancellationToken);
        }
    }

    public async Task SaveReminderAsync(
        int documentId,
        string notifyChannel,
        string contactValue,
        int daysBefore,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        var channel = NormalizeReminderChannel(notifyChannel);
        var contact = NormalizeRequired(contactValue, channel == "EMAIL" ? "Iveskite el. pasta." : "Iveskite telefono numeri.", 320);
        if (daysBefore is < 1 or > 365)
        {
            throw new InvalidOperationException("Pranesimo dienu skaicius turi buti nuo 1 iki 365.");
        }
        ValidateReminderContact(channel, contact);

        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises kurti priminimo prie sio SESHAT iraso.");
        }

        if (!document.ValidTo.HasValue)
        {
            throw new InvalidOperationException("Priminimui reikalinga susitarimo 'Galioja iki' data.");
        }

        var kpiName = BuildReminderKpiName(document, access.UserName, channel);
        var subject = "Seshat informuoja: susitarimo terminas arteja prie pabaigos";
        var body = $"Seshat informuoja: susitarimo terminas arteja prie pabaigos. Susitarimas #{document.Id} '{document.Title}' baigiasi {document.ValidTo:yyyy-MM-dd}.";

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = $"""
            UPDATE {RemindersTableName()}
            SET ACTIVE = 0,
                UPDATED_BY = @UpdatedBy,
                UPDATE_TIME = SYSDATETIMEOFFSET()
            WHERE DOCUMENT_ID = @DocumentId
              AND CREATED_BY = @UpdatedBy
              AND KPI_NAME <> @KpiName;

            IF EXISTS (SELECT 1 FROM {RemindersTableName()} WHERE KPI_NAME = @KpiName)
            BEGIN
                UPDATE {RemindersTableName()}
                SET NOTIFY_CHANNEL = @NotifyChannel,
                    EMAIL = @Email,
                    PHONE_NO = @PhoneNo,
                    DAYS_BEFORE = @DaysBefore,
                    SUBJECT = @Subject,
                    BODY = @Body,
                    ACTIVE = 1,
                    UPDATED_BY = @UpdatedBy,
                    UPDATE_TIME = SYSDATETIMEOFFSET()
                WHERE KPI_NAME = @KpiName;
            END
            ELSE
            BEGIN
                INSERT INTO {RemindersTableName()}
                (
                    DOCUMENT_ID,
                    KPI_NAME,
                    NOTIFY_CHANNEL,
                    EMAIL,
                    PHONE_NO,
                    DAYS_BEFORE,
                    SUBJECT,
                    BODY,
                    ACTIVE,
                    CREATED_BY,
                    CREATED_AT,
                    UPDATED_BY,
                    UPDATE_TIME
                )
                VALUES
                (
                    @DocumentId,
                    @KpiName,
                    @NotifyChannel,
                    @Email,
                    @PhoneNo,
                    @DaysBefore,
                    @Subject,
                    @Body,
                    1,
                    @UpdatedBy,
                    SYSDATETIMEOFFSET(),
                    @UpdatedBy,
                    SYSDATETIMEOFFSET()
                );
            END
            """;
        upsert.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
        upsert.Parameters.Add("@KpiName", SqlDbType.NVarChar, 300).Value = kpiName;
        upsert.Parameters.Add("@NotifyChannel", SqlDbType.NVarChar, 20).Value = channel;
        upsert.Parameters.Add("@Email", SqlDbType.NVarChar, 320).Value = channel == "EMAIL" ? contact : string.Empty;
        upsert.Parameters.Add("@PhoneNo", SqlDbType.NVarChar, 50).Value = channel == "SMS" ? contact : string.Empty;
        upsert.Parameters.Add("@DaysBefore", SqlDbType.Int).Value = daysBefore;
        upsert.Parameters.Add("@Subject", SqlDbType.NVarChar, 300).Value = subject;
        upsert.Parameters.Add("@Body", SqlDbType.NVarChar, -1).Value = body;
        upsert.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = access.UserName;
        await upsert.ExecuteNonQueryAsync(cancellationToken);

        await InsertLogAsync(connection, transaction, documentId, null, "REMINDER_SAVE", access.UserName, $"Paruostas {channel} priminimas {daysBefore} d. iki termino.", null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelReminderAsync(
        int reminderId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var lookup = connection.CreateCommand();
        lookup.CommandText = $"""
            SELECT DOCUMENT_ID
            FROM {RemindersTableName()}
            WHERE ID = @ReminderId
            """;
        lookup.Parameters.Add("@ReminderId", SqlDbType.Int).Value = reminderId;
        var documentIdValue = await lookup.ExecuteScalarAsync(cancellationToken);
        if (documentIdValue is null || documentIdValue is DBNull)
        {
            throw new InvalidOperationException("SESHAT pranesimo poreikis nerastas.");
        }

        var documentId = Convert.ToInt32(documentIdValue);
        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises atsisakyti sio SESHAT pranesimo.");
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"""
            UPDATE {RemindersTableName()}
            SET ACTIVE = 0,
                UPDATED_BY = @UpdatedBy,
                UPDATE_TIME = SYSDATETIMEOFFSET()
            WHERE ID = @ReminderId
            """;
        update.Parameters.Add("@ReminderId", SqlDbType.Int).Value = reminderId;
        update.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = access.UserName;
        await update.ExecuteNonQueryAsync(cancellationToken);

        await InsertLogAsync(connection, transaction, documentId, null, "REMINDER_CANCEL", access.UserName, "Atsisakyta SESHAT pranesimo poreikio.", null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SendReminderTestAsync(
        int documentId,
        int? reminderId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var document = await LoadDocumentAsync(connection, documentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("SESHAT irasas nerastas arba neturite teises siusti testinio pranesimo.");
        }

        var reminder = reminderId.HasValue
            ? (await LoadRemindersAsync(connection, documentId, cancellationToken)).FirstOrDefault(item => item.Id == reminderId.Value)
            : (await LoadRemindersAsync(connection, documentId, cancellationToken)).FirstOrDefault(item => item.Active);
        if (reminder is null)
        {
            throw new InvalidOperationException("Pirma sukurkite EMAIL arba SMS pranesimo nustatyma.");
        }

        if (reminder.NotifyChannel.Equals("SMS", StringComparison.OrdinalIgnoreCase))
        {
            var sms = BuildReminderSmsRequest(document, reminder, isTest: true, createdBy: access.UserName);
            await sendSmsService.SendAsync(sms, cancellationToken);
            await InsertLogWithoutTransactionAsync(connection, documentId, null, "REMINDER_TEST_SEND", access.UserName, $"Issiustas testinis SMS pranesimas: {reminder.PhoneNo}.", cancellationToken);
            return;
        }

        var draft = BuildReminderDraft(document, reminder, isTest: true, createdBy: access.UserName);
        await sendMailService.SendAsync(draft, cancellationToken);
        await InsertLogWithoutTransactionAsync(connection, documentId, null, "REMINDER_TEST_SEND", access.UserName, $"Issiustas testinis EMAIL pranesimas: {reminder.Email}.", cancellationToken);
    }

    public async Task<int> SendDueRemindersAsync(
        DateTime sendDate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                r.ID,
                r.DOCUMENT_ID,
                r.KPI_NAME,
                r.NOTIFY_CHANNEL,
                r.EMAIL,
                r.PHONE_NO,
                r.DAYS_BEFORE,
                r.SUBJECT,
                r.ACTIVE,
                r.CREATED_BY,
                r.CREATED_AT,
                r.UPDATED_BY,
                r.UPDATE_TIME,
                d.TITLE,
                d.DESCRIPTION,
                d.KEYWORDS,
                d.STATUS,
                d.VALID_FROM,
                d.VALID_TO,
                d.CREATED_BY,
                d.CREATED_AT,
                d.UPDATED_BY,
                d.UPDATE_TIME,
                d.REMINDER_DAYS_BEFORE,
                COALESCE(c.COUNTRIES, N'') AS COUNTRIES,
                (
                    SELECT COUNT(1)
                    FROM {FilesTableName()} f
                    WHERE f.DOCUMENT_ID = d.ID
                      AND f.IS_DELETED = 0
                ) AS FILE_COUNT
            FROM {RemindersTableName()} r
            INNER JOIN {DocumentsTableName()} d ON d.ID = r.DOCUMENT_ID
            OUTER APPLY (
                SELECT STUFF((
                    SELECT N',' + dc2.COUNTRY_CODE
                    FROM {DocumentCountriesTableName()} dc2
                    WHERE dc2.DOCUMENT_ID = d.ID
                    ORDER BY dc2.COUNTRY_CODE
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 1, N'') AS COUNTRIES
            ) c
            WHERE r.ACTIVE = 1
              AND r.NOTIFY_CHANNEL = N'EMAIL'
              AND NULLIF(LTRIM(RTRIM(r.EMAIL)), N'') IS NOT NULL
              AND d.STATUS = N'ACTIVE'
              AND d.VALID_TO IS NOT NULL
              AND CAST(DATEADD(day, -r.DAYS_BEFORE, d.VALID_TO) AS date) = @SendDate
            ORDER BY r.ID
            """;
        command.Parameters.Add("@SendDate", SqlDbType.Date).Value = sendDate.Date;

        var due = new List<(SeshatReminderEntry Reminder, SeshatDocumentSummary Document)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var reminder = new SeshatReminderEntry(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetBoolean(8),
                    reader.GetString(9),
                    reader.GetFieldValue<DateTimeOffset>(10),
                    reader.GetString(11),
                    reader.GetFieldValue<DateTimeOffset>(12));
                var document = new SeshatDocumentSummary(
                    reader.GetInt32(1),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                    reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                    SplitCountries(reader.GetString(24)),
                    reader.GetString(19),
                    reader.GetFieldValue<DateTimeOffset>(20),
                    reader.GetString(21),
                    reader.GetFieldValue<DateTimeOffset>(22),
                    reader.GetInt32(25),
                    reader.IsDBNull(23) ? null : reader.GetInt32(23));
                due.Add((reminder, document));
            }
        }

        var sent = 0;
        foreach (var item in due)
        {
            if (await WasReminderAlreadySentAsync(connection, item.Reminder.Id, sendDate, cancellationToken))
            {
                continue;
            }

            try
            {
                if (item.Reminder.NotifyChannel.Equals("SMS", StringComparison.OrdinalIgnoreCase))
                {
                    var sms = BuildReminderSmsRequest(item.Document, item.Reminder, isTest: false, createdBy: "SESHAT_SCHEDULER");
                    await sendSmsService.SendAsync(sms, cancellationToken);
                    await InsertLogWithoutTransactionAsync(connection, item.Document.Id, null, "REMINDER_SEND", "SESHAT_SCHEDULER", $"Issiustas SMS pranesimas {item.Reminder.PhoneNo} pagal grafika {sendDate:yyyy-MM-dd}; reminder:{item.Reminder.Id};", cancellationToken);
                }
                else
                {
                    var draft = BuildReminderDraft(item.Document, item.Reminder, isTest: false, createdBy: "SESHAT_SCHEDULER");
                    await sendMailService.SendAsync(draft, cancellationToken);
                    await InsertLogWithoutTransactionAsync(connection, item.Document.Id, null, "REMINDER_SEND", "SESHAT_SCHEDULER", $"Issiustas EMAIL pranesimas {item.Reminder.Email} pagal grafika {sendDate:yyyy-MM-dd}; reminder:{item.Reminder.Id};", cancellationToken);
                }

                sent++;
            }
            catch (Exception ex)
            {
                var contact = item.Reminder.NotifyChannel.Equals("SMS", StringComparison.OrdinalIgnoreCase) ? item.Reminder.PhoneNo : item.Reminder.Email;
                await InsertLogWithoutTransactionAsync(connection, item.Document.Id, null, "REMINDER_SEND_FAILED", "SESHAT_SCHEDULER", $"Nepavyko issiusti {item.Reminder.NotifyChannel} pranesimo {contact}: {NormalizeOptional(ex.Message, 700)}", cancellationToken);
            }
        }

        return sent;
    }
    public async Task<SeshatFileEntry?> GetFileAsync(
        int fileId,
        SeshatAccessState access,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                f.ID,
                f.DOCUMENT_ID,
                f.ORIGINAL_FILE_NAME,
                f.SECTION_ID,
                f.STORED_FILE_NAME,
                f.CONTENT_TYPE,
                f.FILE_SIZE_BYTES,
                f.FILE_KIND,
                f.DESCRIPTION,
                f.UPLOADED_BY,
                f.UPLOADED_AT,
                f.IS_DELETED,
                f.DELETED_BY,
                f.DELETED_AT,
                f.DELETE_REASON
            FROM {FilesTableName()} f
            INNER JOIN {DocumentCountriesTableName()} dc ON dc.DOCUMENT_ID = f.DOCUMENT_ID
            WHERE f.ID = @FileId
              AND (@IncludeDeleted = 1 OR f.IS_DELETED = 0)
              AND ({BuildAccessPredicate(access, command, "dc.COUNTRY_CODE", requireContribute: false)})
            """;
        command.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId;
        command.Parameters.Add("@IncludeDeleted", SqlDbType.Bit).Value = includeDeleted;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFile(reader) : null;
    }

    public string GetPhysicalFilePath(SeshatFileEntry file)
    {
        var relativePath = file.IsDeleted
            ? Path.Combine("Deleted", "Files", file.DocumentId.ToString(), file.StoredFileName)
            : Path.Combine("Files", file.DocumentId.ToString(), file.StoredFileName);
        return Path.Combine(GetStorageRoot(), relativePath);
    }

    public async Task DeleteFileAsync(
        int fileId,
        string deleteReason,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var file = await LoadFileForUpdateAsync(connection, null, fileId, cancellationToken);
        if (file is null || file.IsDeleted)
        {
            throw new InvalidOperationException("Failas nerastas arba jau istrintas.");
        }

        var document = await LoadDocumentAsync(connection, file.DocumentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises trinti sio failo.");
        }

        var activePath = GetPhysicalFilePath(file);
        var deletedPath = Path.Combine(GetStorageRoot(), "Deleted", "Files", file.DocumentId.ToString(), file.StoredFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(deletedPath)!);
        if (File.Exists(activePath))
        {
            MoveOverwriting(activePath, deletedPath);
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {FilesTableName()}
            SET IS_DELETED = 1,
                DELETED_BY = @DeletedBy,
                DELETED_AT = SYSDATETIMEOFFSET(),
                DELETE_REASON = @DeleteReason,
                DELETED_STORAGE_PATH = @DeletedStoragePath
            WHERE ID = @FileId
            """;
        command.Parameters.Add("@DeletedBy", SqlDbType.NVarChar, 256).Value = access.UserName;
        command.Parameters.Add("@DeleteReason", SqlDbType.NVarChar, 1000).Value = NormalizeOptional(deleteReason, 1000);
        command.Parameters.Add("@DeletedStoragePath", SqlDbType.NVarChar, 1000).Value = Path.Combine("Deleted", "Files", file.DocumentId.ToString(), file.StoredFileName);
        command.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await InsertLogAsync(connection, transaction, file.DocumentId, fileId, "FILE_DELETE", access.UserName, $"Failas perkeltas i Deleted: {file.OriginalFileName}.", file, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RestoreFileAsync(
        int fileId,
        SeshatAccessState access,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var file = await LoadFileForUpdateAsync(connection, null, fileId, cancellationToken);
        if (file is null || !file.IsDeleted)
        {
            throw new InvalidOperationException("Istrintas failas nerastas.");
        }

        var document = await LoadDocumentAsync(connection, file.DocumentId, access, cancellationToken);
        if (document is null || !access.CanContributeAny(document.Countries))
        {
            throw new InvalidOperationException("Neturite teises atstatyti sio failo.");
        }

        var deletedPath = GetPhysicalFilePath(file);
        var activePath = Path.Combine(GetStorageRoot(), "Files", file.DocumentId.ToString(), file.StoredFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        if (File.Exists(deletedPath))
        {
            MoveOverwriting(deletedPath, activePath);
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {FilesTableName()}
            SET IS_DELETED = 0,
                DELETED_BY = NULL,
                DELETED_AT = NULL,
                DELETE_REASON = NULL,
                DELETED_STORAGE_PATH = NULL
            WHERE ID = @FileId
            """;
        command.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await InsertLogAsync(connection, transaction, file.DocumentId, fileId, "FILE_RESTORE", access.UserName, $"Atstatytas failas {file.OriginalFileName}.", file, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }


    private string ResolveSeshatUserName(ClaimsPrincipal user)
    {
        if (IsDevelopmentPasswordSession() &&
            !string.IsNullOrWhiteSpace(_options.DevelopmentPasswordUserName))
        {
            return _options.DevelopmentPasswordUserName.Trim();
        }

        return userNameResolver.ResolveUserName(user);
    }

    private bool IsDevelopmentPasswordSession()
    {
        return environment.IsDevelopment() &&
               httpContextAccessor.HttpContext?.Session.GetString(LoginModel.PasswordSessionKey) == "1";
    }
    private async Task<SeshatDocumentSummary?> LoadDocumentAsync(
        SqlConnection connection,
        int documentId,
        SeshatAccessState access,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                d.ID,
                d.TITLE,
                d.DESCRIPTION,
                d.KEYWORDS,
                d.STATUS,
                d.VALID_FROM,
                d.VALID_TO,
                d.CREATED_BY,
                d.CREATED_AT,
                d.UPDATED_BY,
                d.UPDATE_TIME,
                d.REMINDER_DAYS_BEFORE,
                COALESCE(c.COUNTRIES, N'') AS COUNTRIES,
                (
                    SELECT COUNT(1)
                    FROM {FilesTableName()} f
                    WHERE f.DOCUMENT_ID = d.ID
                      AND f.IS_DELETED = 0
                ) AS FILE_COUNT
            FROM {DocumentsTableName()} d
            OUTER APPLY (
                SELECT STUFF((
                    SELECT N',' + dc2.COUNTRY_CODE
                    FROM {DocumentCountriesTableName()} dc2
                    WHERE dc2.DOCUMENT_ID = d.ID
                    ORDER BY dc2.COUNTRY_CODE
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 1, N'') AS COUNTRIES
            ) c
            WHERE d.ID = @DocumentId
              AND EXISTS (
                    SELECT 1
                    FROM {DocumentCountriesTableName()} ac
                    WHERE ac.DOCUMENT_ID = d.ID
                      AND ({BuildAccessPredicate(access, command, "ac.COUNTRY_CODE", requireContribute: false)})
              )
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocumentSummary(reader) : null;
    }

    private async Task<IReadOnlyList<SeshatFileEntry>> LoadFilesAsync(
        SqlConnection connection,
        int documentId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ID,
                DOCUMENT_ID,
                ORIGINAL_FILE_NAME,
                SECTION_ID,
                STORED_FILE_NAME,
                CONTENT_TYPE,
                FILE_SIZE_BYTES,
                FILE_KIND,
                DESCRIPTION,
                UPLOADED_BY,
                UPLOADED_AT,
                IS_DELETED,
                DELETED_BY,
                DELETED_AT,
                DELETE_REASON
            FROM {FilesTableName()}
            WHERE DOCUMENT_ID = @DocumentId
              AND (@IncludeDeleted = 1 OR IS_DELETED = 0)
            ORDER BY IS_DELETED, UPLOADED_AT DESC, ID DESC
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
        command.Parameters.Add("@IncludeDeleted", SqlDbType.Bit).Value = includeDeleted;

        var rows = new List<SeshatFileEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadFile(reader));
        }

        return rows;
    }

    private async Task<IReadOnlyList<SeshatSectionEntry>> LoadSectionsAsync(
        SqlConnection connection,
        int documentId,
        IReadOnlyList<SeshatFileEntry> files,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ID,
                DOCUMENT_ID,
                TITLE,
                BODY,
                SORT_ORDER,
                CREATED_BY,
                CREATED_AT
            FROM {SectionsTableName()}
            WHERE DOCUMENT_ID = @DocumentId
            ORDER BY SORT_ORDER, ID
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;

        var rows = new List<SeshatSectionEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sectionId = reader.GetInt32(0);
            rows.Add(new SeshatSectionEntry(
                sectionId,
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                files.Where(file => file.SectionId == sectionId).ToArray()));
        }

        return rows;
    }

    private async Task<IReadOnlyList<SeshatReminderEntry>> LoadRemindersAsync(
        SqlConnection connection,
        int documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ID,
                DOCUMENT_ID,
                KPI_NAME,
                NOTIFY_CHANNEL,
                EMAIL,
                PHONE_NO,
                DAYS_BEFORE,
                SUBJECT,
                ACTIVE,
                CREATED_BY,
                CREATED_AT,
                UPDATED_BY,
                UPDATE_TIME
            FROM {RemindersTableName()}
            WHERE DOCUMENT_ID = @DocumentId
            ORDER BY ACTIVE DESC, DAYS_BEFORE, ID DESC
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;

        var rows = new List<SeshatReminderEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SeshatReminderEntry(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetBoolean(8),
                reader.GetString(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.GetString(11),
                reader.GetFieldValue<DateTimeOffset>(12)));
        }

        return rows;
    }
    private async Task<IReadOnlyList<SeshatLogEntry>> LoadLogsAsync(
        SqlConnection connection,
        int documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (@Limit)
                LOG_ID,
                DOCUMENT_ID,
                FILE_ID,
                EVENT_AT,
                ACTION_TYPE,
                ACTOR_USER,
                SUMMARY
            FROM {LogTableName()}
            WHERE DOCUMENT_ID = @DocumentId
            ORDER BY EVENT_AT DESC, LOG_ID DESC
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = Math.Clamp(_options.LogLimit, 1, 200);
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;

        var rows = new List<SeshatLogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SeshatLogEntry(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
        }

        return rows;
    }

    private async Task<SeshatFileEntry?> LoadFileForUpdateAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int fileId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                DOCUMENT_ID,
                ORIGINAL_FILE_NAME,
                SECTION_ID,
                STORED_FILE_NAME,
                CONTENT_TYPE,
                FILE_SIZE_BYTES,
                FILE_KIND,
                DESCRIPTION,
                UPLOADED_BY,
                UPLOADED_AT,
                IS_DELETED,
                DELETED_BY,
                DELETED_AT,
                DELETE_REASON
            FROM {FilesTableName()}
            WHERE ID = @FileId
            """;
        command.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFile(reader) : null;
    }

    private async Task ReplaceCountriesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int documentId,
        IReadOnlyList<string> countries,
        CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = $"DELETE FROM {DocumentCountriesTableName()} WHERE DOCUMENT_ID = @DocumentId";
        delete.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var country in countries)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {DocumentCountriesTableName()} (DOCUMENT_ID, COUNTRY_CODE)
                VALUES (@DocumentId, @CountryCode)
                """;
            insert.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
            insert.Parameters.Add("@CountryCode", SqlDbType.NVarChar, 10).Value = country;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task InsertLogAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int? documentId,
        int? fileId,
        string actionType,
        string actorUser,
        string summary,
        object? snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {LogTableName()}
            (
                DOCUMENT_ID,
                FILE_ID,
                EVENT_AT,
                ACTION_TYPE,
                ACTOR_USER,
                SUMMARY,
                SNAPSHOT_JSON
            )
            VALUES
            (
                @DocumentId,
                @FileId,
                SYSDATETIMEOFFSET(),
                @ActionType,
                @ActorUser,
                @Summary,
                @SnapshotJson
            )
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId.HasValue ? documentId.Value : DBNull.Value;
        command.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId.HasValue ? fileId.Value : DBNull.Value;
        command.Parameters.Add("@ActionType", SqlDbType.NVarChar, 40).Value = actionType;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = actorUser;
        command.Parameters.Add("@Summary", SqlDbType.NVarChar, 1000).Value = summary;
        command.Parameters.Add("@SnapshotJson", SqlDbType.NVarChar, -1).Value =
            snapshot is null ? DBNull.Value : JsonSerializer.Serialize(snapshot, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertLogWithoutTransactionAsync(
        SqlConnection connection,
        int? documentId,
        int? fileId,
        string actionType,
        string actorUser,
        string summary,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {LogTableName()}
            (
                DOCUMENT_ID,
                FILE_ID,
                EVENT_AT,
                ACTION_TYPE,
                ACTOR_USER,
                SUMMARY
            )
            VALUES
            (
                @DocumentId,
                @FileId,
                SYSDATETIMEOFFSET(),
                @ActionType,
                @ActorUser,
                @Summary
            )
            """;
        command.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId.HasValue ? documentId.Value : DBNull.Value;
        command.Parameters.Add("@FileId", SqlDbType.Int).Value = fileId.HasValue ? fileId.Value : DBNull.Value;
        command.Parameters.Add("@ActionType", SqlDbType.NVarChar, 40).Value = actionType;
        command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = actorUser;
        command.Parameters.Add("@Summary", SqlDbType.NVarChar, 1000).Value = summary;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private string BuildAccessPredicate(
        SeshatAccessState access,
        SqlCommand command,
        string countrySql,
        bool requireContribute)
    {
        if (access.IsAdmin)
        {
            return "1 = 1";
        }

        var countries = requireContribute
            ? access.ContributeCountries.ToArray(): access.ViewCountries.Union(access.ContributeCountries, StringComparer.OrdinalIgnoreCase).ToArray();
        var names = countries
            .Where(country => !string.IsNullOrWhiteSpace(country))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((country, index) =>
            {
                var name = $"@AccessCountry{command.Parameters.Count}_{index}";
                command.Parameters.Add(name, SqlDbType.NVarChar, 10).Value = country;
                return name;
            })
            .ToArray();

        return names.Length == 0 ? "1 = 0" : $"{countrySql} IN ({string.Join(", ", names)})";
    }

    private static SeshatDocumentSummary ReadDocumentSummary(SqlDataReader reader)
    {
        return new SeshatDocumentSummary(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            SplitCountries(reader.GetString(12)),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetInt32(13),
            reader.IsDBNull(11) ? null : reader.GetInt32(11));
    }

        private static SeshatFileEntry ReadFile(SqlDataReader reader)
    {
        return new SeshatFileEntry(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }
private static void AddDocumentParameters(
        SqlCommand command,
        string title,
        string description,
        string keywords,
        string status,
        DateTime? validFrom,
        DateTime? validTo,
        int? reminderDaysBefore,
        string userName)
    {
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = title;
        command.Parameters.Add("@Description", SqlDbType.NVarChar, -1).Value = description;
        command.Parameters.Add("@Keywords", SqlDbType.NVarChar, 500).Value = keywords;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = status;
        command.Parameters.Add("@ValidFrom", SqlDbType.Date).Value = validFrom.HasValue ? validFrom.Value.Date : DBNull.Value;
        command.Parameters.Add("@ValidTo", SqlDbType.Date).Value = validTo.HasValue ? validTo.Value.Date : DBNull.Value;
        command.Parameters.Add("@ReminderDaysBefore", SqlDbType.Int).Value = reminderDaysBefore.HasValue ? reminderDaysBefore.Value : DBNull.Value;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = userName;
    }

    private SendMailDraft BuildReminderDraft(
        SeshatDocumentSummary document,
        SeshatReminderEntry reminder,
        bool isTest,
        string createdBy)
    {
        var validTo = document.ValidTo.HasValue ? UiFormat.Date(document.ValidTo.Value) : "nenustatyta";
        var countries = document.Countries.Count == 0 ? "-" : string.Join(", ", document.Countries);
        var testPrefix = isTest ? "TESTINIS PRANESIMAS\n\n" : string.Empty;
        var message = $"""
            {testPrefix}Seshat informuoja: susitarimo terminas arteja prie pabaigos.

            Modelio irasas: SESHAT
            Susitarimas: {document.Title}
            SESHAT ID: {document.Id}
            Salys: {countries}
            Galioja iki: {validTo}
            Pranesama pries: {reminder.DaysBefore} d. iki termino
            """;

        return sendMailService.BuildDraft(
            "SESHAT",
            isTest ? $"Testinis Seshat informuoja: {document.Title}" : $"Seshat informuoja: {document.Title}",
            reminder.Email,
            string.Empty,
            message,
            createdBy);
    }


    private static SendSmsRequest BuildReminderSmsRequest(
        SeshatDocumentSummary document,
        SeshatReminderEntry reminder,
        bool isTest,
        string createdBy)
    {
        var validTo = document.ValidTo.HasValue ? document.ValidTo.Value.ToString("yyyy-MM-dd") : "nenustatyta";
        var prefix = isTest ? "TEST. " : string.Empty;
        var message = $"{prefix}Seshat: susitarimas #{document.Id} '{document.Title}' baigiasi {validTo}. Liko {reminder.DaysBefore} d.";
        message = NormalizeOptional(message, 160);

        return new SendSmsRequest(
            reminder.PhoneNo,
            message,
            createdBy,
            "SESHAT_REMINDER");
    }
    private async Task<bool> WasReminderAlreadySentAsync(
        SqlConnection connection,
        int reminderId,
        DateTime sendDate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) 1
            FROM {LogTableName()}
            WHERE ACTION_TYPE = N'REMINDER_SEND'
              AND SUMMARY LIKE @SummaryLike
              AND CAST(EVENT_AT AS date) = @SendDate
            """;
        command.Parameters.Add("@SummaryLike", SqlDbType.NVarChar, 1000).Value = $"%reminder:{reminderId};%";
        command.Parameters.Add("@SendDate", SqlDbType.Date).Value = sendDate.Date;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }
    private string GetConnectionString()
    {
        return configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.ConnectionStringName}' nesukonfiguruota.");
    }

    private string DocumentsTableName() => SqlName.NormalizeTableName(_options.DocumentsTableName, DefaultDocumentsTableName);

    private string DocumentCountriesTableName() => SqlName.NormalizeTableName(_options.DocumentCountriesTableName, DefaultDocumentCountriesTableName);

    private string SectionsTableName() => SqlName.NormalizeTableName(_options.SectionsTableName, DefaultSectionsTableName);

    private string FilesTableName() => SqlName.NormalizeTableName(_options.FilesTableName, DefaultFilesTableName);

    private string RemindersTableName() => SqlName.NormalizeTableName("BLS_LOGIT_STAT.dbo.SESHAT_REMINDERS", DefaultRemindersTableName);

    private string LogTableName() => SqlName.NormalizeTableName(_options.LogTableName, DefaultLogTableName);

    private string AccessTableName() => SqlName.NormalizeTableName(_options.AccessTableName, DefaultAccessTableName);

    private static string SqlColumn(string columnName) => SqlName.QuoteIdentifier(columnName);

    private string GetStorageRoot()
    {
        var basePath = string.IsNullOrWhiteSpace(_fileUploadOptions.StoragePath)
            ? _options.StoragePath
            : _fileUploadOptions.StoragePath;

        var root = Path.Combine(basePath, "SESHAT");
        return Path.IsPathRooted(root)
            ? root
            : Path.Combine(environment.ContentRootPath, root);
    }
    private string GetActiveFilesDirectory(int documentId)
    {
        return Path.Combine(GetStorageRoot(), "Files", documentId.ToString());
    }

    private static IReadOnlyList<string> ParseCountries(string countriesText)
    {
        return countriesText
            .Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeCountry)
            .Where(country => !string.IsNullOrWhiteSpace(country))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitCountries(string countriesText)
    {
        return countriesText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static string NormalizeCountry(string country)
    {
        return country.Trim().ToUpperInvariant();
    }



    private static void ValidateReminderContact(string channel, string contact)
    {
        if (channel == "EMAIL")
        {
            if (!Regex.IsMatch(contact, @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException("Iveskite korektiska el. pasto adresa, pvz. user@bls.lt.");
            }

            return;
        }

        if (!Regex.IsMatch(contact, @"^\+370-?\d{8}$"))
        {
            throw new InvalidOperationException("SMS pranesimams iveskite lietuviska telefono numeri, pvz. +37061234567.");
        }
    }
    private static string NormalizeReminderChannel(string value)
    {
        var channel = NormalizeOptional(value, 20).ToUpperInvariant();
        return channel is "EMAIL" or "SMS" ? channel : throw new InvalidOperationException("Pasirinkite pranesimo buda: EMAIL arba SMS.");
    }

    private static string BuildReminderKpiName(SeshatDocumentSummary document, string userName, string channel)
    {
        var countryPart = document.Countries.Count == 0
            ? "NA"
            : string.Join("_", document.Countries.Select(NormalizeKpiPart));
        return $"{countryPart}_SESHAT_{document.Id}_{NormalizeKpiPart(userName)}_{channel}";
    }

    private static string NormalizeKpiPart(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());
        normalized = Regex.Replace(normalized, "_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "NA" : normalized;
    }
    private static string NormalizeStatus(string status)
    {
        var normalized = NormalizeOptional(status).ToUpperInvariant();
        return normalized is "DRAFT" or "ACTIVE" or "EXPIRED" or "REPLACED" or "ARCHIVED"
            ? normalized
            : "ACTIVE";
    }

    private static string NormalizeFileKind(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".msg" => "OUTLOOK_MSG",
            ".eml" => "OUTLOOK_EML",
            ".xlsx" or ".xlsm" or ".xls" or ".csv" => "EXCEL",
            ".pdf" => "PDF",
            ".doc" or ".docx" => "WORD",
            _ => "FILE"
        };
    }

    private static string NormalizeRequired(string value, string errorMessage, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? throw new InvalidOperationException(errorMessage) : normalized;
    }

    private static string NormalizeOptional(string? value, int maxLength = 200)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string BuildSafeFileName(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = $"seshat_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            safeFileName = safeFileName.Replace(invalidCharacter, '_');
        }

        return safeFileName.Trim();
    }

    private static void MoveOverwriting(string source, string destination)
    {
        if (File.Exists(destination))
        {
            var info = new FileInfo(destination);
            var duplicatePath = Path.Combine(
                info.DirectoryName!,
                $"OLD_{DateTime.Now:yyyyMMdd_HHmmss}_{info.Name}");
            File.Move(destination, duplicatePath);
        }

        File.Move(source, destination);
    }
}

















