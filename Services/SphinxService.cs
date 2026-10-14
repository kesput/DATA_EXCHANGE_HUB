using System.Data;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class SphinxService(
    IConfiguration configuration,
    IOptions<SphinxOptions> options,
    IUserNameResolver userNameResolver) : ISphinxService
{
    private readonly SphinxOptions _options = options.Value;
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ─── Table name helpers ──────────────────────────────────────────────────

    private string SurveyTable => SqlName.NormalizeTableName(_options.SurveyTableName, "BLS_LOGIT_STAT.dbo.SPHINX_SURVEYS");
    private string QuestionTable => SqlName.NormalizeTableName(_options.QuestionTableName, "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_QUESTIONS");
    private string OptionTable => SqlName.NormalizeTableName(_options.OptionTableName, "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_OPTIONS");
    private string ResponseTable => SqlName.NormalizeTableName(_options.ResponseTableName, "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_RESPONSES");
    private string AnswerTable => SqlName.NormalizeTableName(_options.AnswerTableName, "BLS_LOGIT_STAT.dbo.SPHINX_SURVEY_ANSWERS");

    private SqlConnection OpenConnection()
    {
        var cs = configuration.GetConnectionString(_options.ConnectionStringName)
            ?? throw new InvalidOperationException($"Prisijungimo eilutė '{_options.ConnectionStringName}' nesukonfiguruota.");
        return new SqlConnection(cs);
    }

    public async Task<SphinxAccessState> GetAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var isUser = await HasAccessAsync(_options.AppName, userName, cancellationToken);
        var isAdmin = await HasAccessAsync(_options.AdminAppName, userName, cancellationToken);

        return new SphinxAccessState(isUser || isAdmin, isAdmin);
    }

    private async Task<bool> HasAccessAsync(
        string appName,
        string userName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        var connectionString = configuration.GetConnectionString(_options.AccessConnectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{_options.AccessConnectionStringName}' nesukonfiguruota.");
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            DefaultAccessProcedureName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = appName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    // ─── CreateSurveyAsync ───────────────────────────────────────────────────

    public async Task CreateSurveyAsync(
        SphinxCreateSurveyRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = JsonSerializer.Deserialize<SphinxDefinitionJson>(request.DefinitionJson, JsonOpts)
            ?? throw new InvalidOperationException("Neteisinga apklausos struktūra.");

        var validQuestions = definition.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList();
        var surveyKind = NormalizeSurveyKind(request.SurveyKind);
        ValidateDefinitionForKind(validQuestions, surveyKind);
        if (validQuestions.Count == 0)
            throw new InvalidOperationException("Apklausa turi turėti bent vieną klausimą.");

        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        int surveyId;
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {SurveyTable} (TITLE, DESCRIPTION, CREATED_BY, CREATED_AT, STATUS, MAX_RESPONSES, EXPIRES_AT, IS_ANONYMOUS, SURVEY_KIND)
                OUTPUT INSERTED.ID
                VALUES (@Title, @Desc, @By, SYSDATETIMEOFFSET(), N'OPEN', @MaxResp, @ExpiresAt, @IsAnonymous, @SurveyKind);
                """;
            cmd.Parameters.Add("@Title", SqlDbType.NVarChar, 500).Value = request.Title;
            cmd.Parameters.Add("@Desc", SqlDbType.NVarChar, 2000).Value =
                string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description;
            cmd.Parameters.Add("@By", SqlDbType.NVarChar, 100).Value = request.CreatedBy;
            cmd.Parameters.Add("@MaxResp", SqlDbType.Int).Value =
                request.MaxResponses.HasValue ? request.MaxResponses.Value : DBNull.Value;
            cmd.Parameters.Add("@ExpiresAt", SqlDbType.DateTimeOffset).Value =
                request.ExpiresAt.HasValue ? request.ExpiresAt.Value : DBNull.Value;
            cmd.Parameters.Add("@IsAnonymous", SqlDbType.Bit).Value = request.IsAnonymous;
            cmd.Parameters.Add("@SurveyKind", SqlDbType.NVarChar, 20).Value = surveyKind;
            surveyId = (int)(await cmd.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Apklausos įrašo sukurti nepavyko."));
        }

        var flatQuestions = new List<FlatQ>();
        var flatOptions = new List<FlatO>();
        var topOrder = 0;
        foreach (var q in validQuestions)
            Flatten(q, topOrder++, null, flatQuestions, flatOptions);

        var questionMap = new Dictionary<string, int>();
        foreach (var q in flatQuestions)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {QuestionTable} (SURVEY_ID, PARENT_OPTION_ID, QUESTION_TEXT, QUESTION_TYPE, TEXT_VALIDATION, QUESTION_ORDER)
                OUTPUT INSERTED.ID
                VALUES (@SurveyId, NULL, @Text, @Type, @TextValidation, @Order);
                """;
            cmd.Parameters.Add("@SurveyId", SqlDbType.Int).Value = surveyId;
            cmd.Parameters.Add("@Text", SqlDbType.NVarChar, 1000).Value = q.Text;
            cmd.Parameters.Add("@Type", SqlDbType.NVarChar, 50).Value = q.Type;
            cmd.Parameters.Add("@TextValidation", SqlDbType.NVarChar, 20).Value = q.TextValidation;
            cmd.Parameters.Add("@Order", SqlDbType.Int).Value = q.Order;
            var dbId = (int)(await cmd.ExecuteScalarAsync(cancellationToken)!);
            questionMap[q.ClientId] = dbId;
        }

        var optionMap = new Dictionary<string, int>();
        foreach (var o in flatOptions)
        {
            int? skipToId = null;
            if (o.SkipAction == "JUMP" && o.SkipToClientId != null &&
                questionMap.TryGetValue(o.SkipToClientId, out var resolvedId))
                skipToId = resolvedId;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {OptionTable} (QUESTION_ID, OPTION_TEXT, OPTION_ORDER, SKIP_ACTION, SKIP_TO_QUESTION_ID)
                OUTPUT INSERTED.ID
                VALUES (@QId, @Text, @Order, @Skip, @SkipTo);
                """;
            cmd.Parameters.Add("@QId", SqlDbType.Int).Value = questionMap[o.QuestionClientId];
            cmd.Parameters.Add("@Text", SqlDbType.NVarChar, 500).Value = o.Text;
            cmd.Parameters.Add("@Order", SqlDbType.Int).Value = o.Order;
            cmd.Parameters.Add("@Skip", SqlDbType.NVarChar, 20).Value = o.SkipAction;
            cmd.Parameters.Add("@SkipTo", SqlDbType.Int).Value = skipToId.HasValue ? skipToId.Value : DBNull.Value;
            var dbOptId = (int)(await cmd.ExecuteScalarAsync(cancellationToken)!);
            optionMap[o.ClientId] = dbOptId;
        }

        foreach (var q in flatQuestions.Where(q => q.ParentOptionClientId != null))
        {
            if (!optionMap.TryGetValue(q.ParentOptionClientId!, out var parentOptDbId)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                UPDATE {QuestionTable}
                SET PARENT_OPTION_ID = @ParentOptId
                WHERE ID = @QId;
                """;
            cmd.Parameters.Add("@ParentOptId", SqlDbType.Int).Value = parentOptDbId;
            cmd.Parameters.Add("@QId", SqlDbType.Int).Value = questionMap[q.ClientId];
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ─── GetSurveyCardsAsync ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<SphinxSurveyCard>> GetSurveyCardsAsync(
        string respondent,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                s.ID,
                s.TITLE,
                s.DESCRIPTION,
                s.STATUS,
                s.MAX_RESPONSES,
                s.EXPIRES_AT,
                s.IS_ANONYMOUS,
                COALESCE(s.SURVEY_KIND, N'SURVEY') AS SURVEY_KIND,
                (SELECT COUNT(*) FROM {QuestionTable} q
                 WHERE q.SURVEY_ID = s.ID AND q.PARENT_OPTION_ID IS NULL) AS QUESTION_COUNT,
                (SELECT COUNT(*) FROM {ResponseTable} r
                 WHERE r.SURVEY_ID = s.ID) AS RESPONSE_COUNT,
                (SELECT COUNT(*) FROM {ResponseTable} r2
                 WHERE r2.SURVEY_ID = s.ID AND r2.RESPONDENT = @Respondent) AS HAS_RESPONDED
            FROM {SurveyTable} s
            WHERE s.STATUS = N'OPEN'
              AND (s.EXPIRES_AT IS NULL OR s.EXPIRES_AT > SYSDATETIMEOFFSET())
              AND COALESCE(s.SURVEY_KIND, N'SURVEY') = N'SURVEY'
            ORDER BY s.CREATED_AT DESC;
            """;
        cmd.Parameters.Add("@Respondent", SqlDbType.NVarChar, 100).Value = respondent;

        var cards = new List<SphinxSurveyCard>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var maxResp = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
            var responseCount = reader.GetInt32(9);
            if (maxResp.HasValue && responseCount >= maxResp.Value) continue;

            cards.Add(new SphinxSurveyCard(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(8),
                responseCount,
                reader.GetInt32(10) > 0,
                maxResp,
                reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(5),
                reader.GetBoolean(6),
                reader.GetString(7)));
        }
        return cards;
    }

    public async Task<SphinxSurveyDetail?> GetLatestQuickPollAsync(
        string respondent,
        CancellationToken cancellationToken = default)
    {
        var polls = await GetQuickPollCardsAsync(respondent, cancellationToken);
        return polls.Count > 0
            ? await GetSurveyDetailAsync(polls[0].Id, respondent, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<SphinxSurveyCard>> GetQuickPollCardsAsync(
        string respondent,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                s.ID,
                s.TITLE,
                s.DESCRIPTION,
                s.STATUS,
                s.MAX_RESPONSES,
                s.EXPIRES_AT,
                s.IS_ANONYMOUS,
                COALESCE(s.SURVEY_KIND, N'SURVEY') AS SURVEY_KIND,
                (SELECT COUNT(*) FROM {QuestionTable} q
                 WHERE q.SURVEY_ID = s.ID AND q.PARENT_OPTION_ID IS NULL) AS QUESTION_COUNT,
                (SELECT COUNT(*) FROM {ResponseTable} r
                 WHERE r.SURVEY_ID = s.ID) AS RESPONSE_COUNT,
                (SELECT COUNT(*) FROM {ResponseTable} r2
                 WHERE r2.SURVEY_ID = s.ID AND r2.RESPONDENT = @Respondent) AS HAS_RESPONDED
            FROM {SurveyTable} s
            WHERE s.STATUS = N'OPEN'
              AND COALESCE(s.SURVEY_KIND, N'SURVEY') = N'POLL'
              AND (s.EXPIRES_AT IS NULL OR s.EXPIRES_AT > SYSDATETIMEOFFSET())
              AND (
                    s.MAX_RESPONSES IS NULL OR
                    (SELECT COUNT(*) FROM {ResponseTable} r WHERE r.SURVEY_ID = s.ID) < s.MAX_RESPONSES OR
                    EXISTS (
                        SELECT 1 FROM {ResponseTable} r2
                        WHERE r2.SURVEY_ID = s.ID AND r2.RESPONDENT = @Respondent
                    )
                  )
            ORDER BY s.CREATED_AT DESC;
            """;
        cmd.Parameters.Add("@Respondent", SqlDbType.NVarChar, 100).Value = respondent;

        var cards = new List<SphinxSurveyCard>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cards.Add(new SphinxSurveyCard(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10) > 0,
                reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(5),
                reader.GetBoolean(6),
                reader.GetString(7)));
        }

        return cards;
    }

    public async Task<IReadOnlyDictionary<int, int>> GetQuickPollResultsAsync(
        int surveyId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT a.OPTION_ID, COUNT(*) AS VOTES
            FROM {AnswerTable} a
            INNER JOIN {ResponseTable} r ON r.ID = a.RESPONSE_ID
            WHERE r.SURVEY_ID = @SurveyId
              AND a.OPTION_ID IS NOT NULL
            GROUP BY a.OPTION_ID;
            """;
        cmd.Parameters.Add("@SurveyId", SqlDbType.Int).Value = surveyId;

        var results = new Dictionary<int, int>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results[reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return results;
    }

    public async Task<SphinxSurveyDetail?> GetSurveyForOwnerAsync(
        int id,
        string currentUserName,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)
            FROM {SurveyTable}
            WHERE ID = @Id
              AND (@IsAdmin = 1 OR CREATED_BY = @CurrentUser);
            """;
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@IsAdmin", SqlDbType.Bit).Value = isAdmin;
        cmd.Parameters.Add("@CurrentUser", SqlDbType.NVarChar, 100).Value = currentUserName;

        var canRead = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
        return canRead
            ? await GetSurveyDetailAsync(id, currentUserName, cancellationToken)
            : null;
    }

    // ─── GetSurveyDetailAsync ────────────────────────────────────────────────

    public async Task<SphinxSurveyDetail?> GetSurveyDetailAsync(
        int id,
        string respondent,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        // Load survey
        SphinxSurvey? survey;
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT ID, TITLE, DESCRIPTION, CREATED_BY, CREATED_AT, STATUS, MAX_RESPONSES, EXPIRES_AT, IS_ANONYMOUS, COALESCE(SURVEY_KIND, N'SURVEY')
                FROM {SurveyTable}
                WHERE ID = @Id;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken)) return null;
            survey = new SphinxSurvey(
                r.GetInt32(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), r.GetDateTimeOffset(4), r.GetString(5),
                r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                r.IsDBNull(7) ? (DateTimeOffset?)null : r.GetDateTimeOffset(7),
                r.GetBoolean(8),
                r.GetString(9));
        }

        // Response count + has responded
        int responseCount;
        bool hasResponded;
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT
                    (SELECT COUNT(*) FROM {ResponseTable} WHERE SURVEY_ID = @Id),
                    (SELECT COUNT(*) FROM {ResponseTable} WHERE SURVEY_ID = @Id AND RESPONDENT = @Respondent);
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            cmd.Parameters.Add("@Respondent", SqlDbType.NVarChar, 100).Value = respondent;
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            await r.ReadAsync(cancellationToken);
            responseCount = r.GetInt32(0);
            hasResponded = r.GetInt32(1) > 0;
        }

        // Load all questions
        var questionNodes = new Dictionary<int, SphinxQuestionNode>();
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT ID, QUESTION_TEXT, QUESTION_TYPE, TEXT_VALIDATION, QUESTION_ORDER, PARENT_OPTION_ID
                FROM {QuestionTable}
                WHERE SURVEY_ID = @Id
                ORDER BY QUESTION_ORDER;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var node = new SphinxQuestionNode
                {
                    Id = r.GetInt32(0),
                    Text = r.GetString(1),
                    Type = r.GetString(2),
                    TextValidation = r.GetString(3),
                    Order = r.GetInt32(4),
                    ParentOptionId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5)
                };
                questionNodes[node.Id] = node;
            }
        }

        // Load all options
        var optionNodes = new Dictionary<int, SphinxOptionNode>();
        if (questionNodes.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT o.ID, o.QUESTION_ID, o.OPTION_TEXT, o.OPTION_ORDER, o.SKIP_ACTION, o.SKIP_TO_QUESTION_ID
                FROM {OptionTable} o
                INNER JOIN {QuestionTable} q ON q.ID = o.QUESTION_ID
                WHERE q.SURVEY_ID = @Id
                ORDER BY o.OPTION_ORDER;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var node = new SphinxOptionNode
                {
                    Id = r.GetInt32(0),
                    Text = r.GetString(2),
                    Order = r.GetInt32(3),
                    SkipAction = r.GetString(4),
                    SkipToQuestionId = r.IsDBNull(5) ? (int?)null : r.GetInt32(5)
                };
                optionNodes[node.Id] = node;
                if (questionNodes.TryGetValue(r.GetInt32(1), out var qNode))
                    qNode.Options.Add(node);
            }
        }

        // Wire sub-questions to options
        foreach (var qNode in questionNodes.Values.Where(q => q.ParentOptionId.HasValue))
        {
            if (optionNodes.TryGetValue(qNode.ParentOptionId!.Value, out var optNode))
                optNode.SubQuestions.Add(qNode);
        }

        var topLevel = questionNodes.Values
            .Where(q => q.ParentOptionId is null)
            .OrderBy(q => q.Order)
            .ToList();

        return new SphinxSurveyDetail(survey, responseCount, hasResponded, topLevel);
    }

    // ─── SubmitResponseAsync ─────────────────────────────────────────────────

    public async Task SubmitResponseAsync(
        SphinxSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        var answers = JsonSerializer.Deserialize<List<SphinxAnswerJson>>(request.AnswersJson, JsonOpts)
            ?? throw new InvalidOperationException("Neteisinga atsakymų struktūra.");

        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);
        await using var tx = conn.BeginTransaction();

        try
        {
            // Check survey is still active
            int? maxResp;
            string status;
            DateTimeOffset? expiresAt;
            bool isAnonymous;
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    SELECT STATUS, MAX_RESPONSES, EXPIRES_AT, IS_ANONYMOUS,
                           (SELECT COUNT(*) FROM {ResponseTable} WHERE SURVEY_ID = @Id) AS RESP_COUNT
                    FROM {SurveyTable} WHERE ID = @Id;
                    """;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = request.SurveyId;
                await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await r.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Apklausa nerasta.");
                status = r.GetString(0);
                maxResp = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                expiresAt = r.IsDBNull(2) ? (DateTimeOffset?)null : r.GetDateTimeOffset(2);
                isAnonymous = r.GetBoolean(3);
                var respCount = r.GetInt32(4);

                if (!status.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Apklausa jau uždaryta.");
                if (expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow)
                    throw new InvalidOperationException("Apklausos terminas pasibaigė.");

                // Check max only for NEW respondents
                bool isUpdate;
                {
                    await r.CloseAsync();
                    await using var cmd2 = conn.CreateCommand();
                    cmd2.Transaction = tx;
                    cmd2.CommandText = $"""
                        SELECT COUNT(*) FROM {ResponseTable}
                        WHERE SURVEY_ID = @Id AND RESPONDENT = @Resp;
                        """;
                    cmd2.Parameters.Add("@Id", SqlDbType.Int).Value = request.SurveyId;
                    cmd2.Parameters.Add("@Resp", SqlDbType.NVarChar, 100).Value = request.Respondent;
                    isUpdate = (int)(await cmd2.ExecuteScalarAsync(cancellationToken)!) > 0;
                }

                if (!isUpdate && maxResp.HasValue && respCount >= maxResp.Value)
                    throw new InvalidOperationException("Apklausa jau surinko maksimalų respondentų skaičių.");
            }

            if (isAnonymous)
            {
                await using var duplicateCmd = conn.CreateCommand();
                duplicateCmd.Transaction = tx;
                duplicateCmd.CommandText = $"""
                    SELECT COUNT(*) FROM {ResponseTable}
                    WHERE SURVEY_ID = @Id AND RESPONDENT = @Resp;
                    """;
                duplicateCmd.Parameters.Add("@Id", SqlDbType.Int).Value = request.SurveyId;
                duplicateCmd.Parameters.Add("@Resp", SqlDbType.NVarChar, 100).Value = request.Respondent;
                if ((int)(await duplicateCmd.ExecuteScalarAsync(cancellationToken)!) > 0)
                {
                    throw new InvalidOperationException("Anonimine apklausa jau uzpildyta. Pakartotinai pildyti negalima.");
                }
            }
            else if (string.IsNullOrWhiteSpace(request.Respondent))
            {
                throw new InvalidOperationException("Neanoniminei apklausai nepavyko nustatyti Windows vartotojo.");
            }

            await ValidateTextAnswersAsync(conn, tx, request.SurveyId, answers, cancellationToken);

            // Upsert response header
            int responseId;
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    MERGE {ResponseTable} AS tgt
                    USING (SELECT @SurveyId AS SURVEY_ID, @Resp AS RESPONDENT) AS src
                    ON tgt.SURVEY_ID = src.SURVEY_ID AND tgt.RESPONDENT = src.RESPONDENT
                    WHEN MATCHED THEN
                        UPDATE SET ANSWERED_AT = SYSDATETIMEOFFSET()
                    WHEN NOT MATCHED THEN
                        INSERT (SURVEY_ID, RESPONDENT, ANSWERED_AT)
                        VALUES (@SurveyId, @Resp, SYSDATETIMEOFFSET());
                    SELECT ID FROM {ResponseTable} WHERE SURVEY_ID = @SurveyId AND RESPONDENT = @Resp;
                    """;
                cmd.Parameters.Add("@SurveyId", SqlDbType.Int).Value = request.SurveyId;
                cmd.Parameters.Add("@Resp", SqlDbType.NVarChar, 100).Value = request.Respondent;
                responseId = (int)(await cmd.ExecuteScalarAsync(cancellationToken)!);
            }

            // Delete existing answers for this response
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {AnswerTable} WHERE RESPONSE_ID = @RespId;";
                cmd.Parameters.Add("@RespId", SqlDbType.Int).Value = responseId;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Insert new answers
            foreach (var answer in answers)
            {
                if (answer.OptionIds.Count == 0)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"""
                        INSERT INTO {AnswerTable} (RESPONSE_ID, QUESTION_ID, ANSWER_TEXT, OPTION_ID)
                        VALUES (@RespId, @QId, @Text, NULL);
                        """;
                    cmd.Parameters.Add("@RespId", SqlDbType.Int).Value = responseId;
                    cmd.Parameters.Add("@QId", SqlDbType.Int).Value = answer.QuestionId;
                    cmd.Parameters.Add("@Text", SqlDbType.NVarChar, -1).Value =
                        string.IsNullOrWhiteSpace(answer.Text) ? DBNull.Value : answer.Text.Trim();
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    foreach (var optId in answer.OptionIds)
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = $"""
                            INSERT INTO {AnswerTable} (RESPONSE_ID, QUESTION_ID, ANSWER_TEXT, OPTION_ID)
                            VALUES (@RespId, @QId, NULL, @OptId);
                            """;
                        cmd.Parameters.Add("@RespId", SqlDbType.Int).Value = responseId;
                        cmd.Parameters.Add("@QId", SqlDbType.Int).Value = answer.QuestionId;
                        cmd.Parameters.Add("@OptId", SqlDbType.Int).Value = optId;
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }

            // Auto-close if max responses reached (count after this new response)
            if (maxResp.HasValue)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    UPDATE {SurveyTable}
                    SET STATUS = N'CLOSED'
                    WHERE ID = @Id
                      AND (SELECT COUNT(*) FROM {ResponseTable} WHERE SURVEY_ID = @Id) >= @Max;
                    """;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = request.SurveyId;
                cmd.Parameters.Add("@Max", SqlDbType.Int).Value = maxResp.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ─── GetManagementListAsync ──────────────────────────────────────────────

    public async Task<IReadOnlyList<SphinxManagementItem>> GetManagementListAsync(
        string currentUserName,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                s.ID, s.TITLE, s.STATUS, s.CREATED_BY, s.CREATED_AT,
                s.MAX_RESPONSES, s.EXPIRES_AT,
                COALESCE(s.SURVEY_KIND, N'SURVEY') AS SURVEY_KIND,
                (SELECT COUNT(*) FROM {QuestionTable} q
                 WHERE q.SURVEY_ID = s.ID AND q.PARENT_OPTION_ID IS NULL) AS QUESTION_COUNT,
                (SELECT COUNT(*) FROM {ResponseTable} r
                 WHERE r.SURVEY_ID = s.ID) AS RESPONSE_COUNT
            FROM {SurveyTable} s
            WHERE (@IsAdmin = 1 OR s.CREATED_BY = @CurrentUser)
            ORDER BY s.CREATED_AT DESC;
            """;
        cmd.Parameters.Add("@IsAdmin", SqlDbType.Bit).Value = isAdmin;
        cmd.Parameters.Add("@CurrentUser", SqlDbType.NVarChar, 100).Value = currentUserName;

        var items = new List<SphinxManagementItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SphinxManagementItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTimeOffset(4),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(6),
                reader.GetString(7)));
        }
        return items;
    }

    // ─── CloseSurveyAsync ────────────────────────────────────────────────────

    public async Task CloseSurveyAsync(
        int id,
        string currentUserName,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {SurveyTable}
            SET STATUS = N'CLOSED'
            WHERE ID = @Id
              AND STATUS = N'OPEN'
              AND (@IsAdmin = 1 OR CREATED_BY = @CurrentUser);
            """;
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@IsAdmin", SqlDbType.Bit).Value = isAdmin;
        cmd.Parameters.Add("@CurrentUser", SqlDbType.NVarChar, 100).Value = currentUserName;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SphinxResultsExport?> GetResultsExportAsync(
        int id,
        string currentUserName,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await using var conn = OpenConnection();
        await conn.OpenAsync(cancellationToken);

        string title;
        bool isAnonymous;
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT TITLE, IS_ANONYMOUS
                FROM {SurveyTable}
                WHERE ID = @Id
                  AND (@IsAdmin = 1 OR CREATED_BY = @CurrentUser);
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            cmd.Parameters.Add("@IsAdmin", SqlDbType.Bit).Value = isAdmin;
            cmd.Parameters.Add("@CurrentUser", SqlDbType.NVarChar, 100).Value = currentUserName;

            await using var surveyReader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await surveyReader.ReadAsync(cancellationToken))
            {
                return null;
            }

            title = surveyReader.GetString(0);
            isAnonymous = surveyReader.GetBoolean(1);
        }

        var headers = new[]
        {
            "Order",
            "ParentOptionKey",
            "QuestionKey",
            "QuestionText",
            "Type",
            "TextValidation",
            "OptionKey",
            "OptionText",
            "SelectedAnswer",
            "AnswerText",
            "SkipAction",
            "SkipToQuestionKey",
            "Respondent",
            "AnsweredAt"
        };

        var questionNodes = new Dictionary<int, SphinxQuestionNode>();
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT ID, QUESTION_TEXT, QUESTION_TYPE, TEXT_VALIDATION, QUESTION_ORDER, PARENT_OPTION_ID
                FROM {QuestionTable}
                WHERE SURVEY_ID = @Id
                ORDER BY QUESTION_ORDER, ID;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var node = new SphinxQuestionNode
                {
                    Id = reader.GetInt32(0),
                    Text = reader.GetString(1),
                    Type = reader.GetString(2),
                    TextValidation = reader.GetString(3),
                    Order = reader.GetInt32(4),
                    ParentOptionId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
                };
                questionNodes[node.Id] = node;
            }
        }

        var optionNodes = new Dictionary<int, SphinxOptionNode>();
        if (questionNodes.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT o.ID, o.QUESTION_ID, o.OPTION_TEXT, o.OPTION_ORDER, o.SKIP_ACTION, o.SKIP_TO_QUESTION_ID
                FROM {OptionTable} o
                INNER JOIN {QuestionTable} q ON q.ID = o.QUESTION_ID
                WHERE q.SURVEY_ID = @Id
                ORDER BY q.QUESTION_ORDER, o.OPTION_ORDER, o.ID;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var node = new SphinxOptionNode
                {
                    Id = reader.GetInt32(0),
                    Text = reader.GetString(2),
                    Order = reader.GetInt32(3),
                    SkipAction = reader.GetString(4),
                    SkipToQuestionId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
                };
                optionNodes[node.Id] = node;
                if (questionNodes.TryGetValue(reader.GetInt32(1), out var questionNode))
                {
                    questionNode.Options.Add(node);
                }
            }
        }

        foreach (var questionNode in questionNodes.Values.Where(question => question.ParentOptionId.HasValue))
        {
            if (optionNodes.TryGetValue(questionNode.ParentOptionId!.Value, out var parentOption))
            {
                parentOption.SubQuestions.Add(questionNode);
            }
        }

        var topLevelQuestions = questionNodes.Values
            .Where(question => question.ParentOptionId is null)
            .OrderBy(question => question.Order)
            .ThenBy(question => question.Id)
            .ToArray();

        var questionKeys = BuildResultQuestionKeys(topLevelQuestions);
        var responses = new List<SphinxResultResponse>();
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT ID, RESPONDENT, ANSWERED_AT
                FROM {ResponseTable}
                WHERE SURVEY_ID = @Id
                ORDER BY ANSWERED_AT, ID;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                responses.Add(new SphinxResultResponse(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetDateTimeOffset(2)));
            }
        }

        var selectedOptionsByResponse = new Dictionary<int, HashSet<int>>();
        var textAnswersByResponse = new Dictionary<int, Dictionary<int, string>>();
        if (responses.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT a.RESPONSE_ID, a.QUESTION_ID, a.OPTION_ID, a.ANSWER_TEXT
                FROM {AnswerTable} a
                INNER JOIN {ResponseTable} r ON r.ID = a.RESPONSE_ID
                WHERE r.SURVEY_ID = @Id
                ORDER BY a.RESPONSE_ID, a.ID;
                """;
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var responseId = reader.GetInt32(0);
                var questionId = reader.GetInt32(1);

                if (!reader.IsDBNull(2))
                {
                    if (!selectedOptionsByResponse.TryGetValue(responseId, out var selectedOptions))
                    {
                        selectedOptions = [];
                        selectedOptionsByResponse[responseId] = selectedOptions;
                    }

                    selectedOptions.Add(reader.GetInt32(2));
                    continue;
                }

                if (!textAnswersByResponse.TryGetValue(responseId, out var textAnswers))
                {
                    textAnswers = new Dictionary<int, string>();
                    textAnswersByResponse[responseId] = textAnswers;
                }

                textAnswers[questionId] = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            }
        }

        var rows = new List<IReadOnlyDictionary<string, string?>>();
        var respondentLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var response in responses)
        {
            var respondentKey = string.IsNullOrWhiteSpace(response.Respondent)
                ? response.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : response.Respondent;
            var respondentLabel = isAnonymous
                ? GetAnonymousRespondentLabel(respondentLabels, respondentKey)
                : string.IsNullOrWhiteSpace(response.Respondent) ? "Neatpazintas vartotojas" : response.Respondent;
            var resultContext = new SphinxResultContext(
                response.Id,
                respondentLabel,
                response.AnsweredAt.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                selectedOptionsByResponse.TryGetValue(response.Id, out var selectedOptions) ? selectedOptions : [],
                textAnswersByResponse.TryGetValue(response.Id, out var textAnswers) ? textAnswers : new Dictionary<int, string>());

            var order = 1;
            foreach (var question in topLevelQuestions)
            {
                AppendResultQuestionRows(
                    question,
                    order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    string.Empty,
                    questionKeys,
                    resultContext,
                    rows);
                order++;
            }
        }

        return new SphinxResultsExport(BuildResultsFileName(id, title), headers, rows);
    }

    private static Dictionary<int, string> BuildResultQuestionKeys(IReadOnlyList<SphinxQuestionNode> topLevelQuestions)
    {
        var questionKeys = new Dictionary<int, string>();
        for (var i = 0; i < topLevelQuestions.Count; i++)
        {
            AssignResultQuestionKeys(
                topLevelQuestions[i],
                $"Q{i + 1}",
                questionKeys);
        }

        return questionKeys;
    }

    private static void AssignResultQuestionKeys(
        SphinxQuestionNode question,
        string questionKey,
        Dictionary<int, string> questionKeys)
    {
        questionKeys[question.Id] = questionKey;

        var optionIndex = 1;
        foreach (var option in question.Options.OrderBy(option => option.Order).ThenBy(option => option.Id))
        {
            var optionKey = $"{questionKey}_A{optionIndex}";
            var subIndex = 1;
            foreach (var subQuestion in option.SubQuestions.OrderBy(sub => sub.Order).ThenBy(sub => sub.Id))
            {
                AssignResultQuestionKeys(
                    subQuestion,
                    $"{optionKey}_SUB{subIndex}",
                    questionKeys);
                subIndex++;
            }

            optionIndex++;
        }
    }

    private static void AppendResultQuestionRows(
        SphinxQuestionNode question,
        string order,
        string parentOptionKey,
        IReadOnlyDictionary<int, string> questionKeys,
        SphinxResultContext context,
        List<IReadOnlyDictionary<string, string?>> rows)
    {
        var questionKey = questionKeys.TryGetValue(question.Id, out var key)
            ? key
            : $"Q{question.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        if (question.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) || question.Options.Count == 0)
        {
            var hasTextAnswer = context.TextAnswers.TryGetValue(question.Id, out var answerText);
            rows.Add(ResultRow(
                order,
                parentOptionKey,
                questionKey,
                question.Text,
                question.Type,
                question.TextValidation,
                string.Empty,
                string.Empty,
                hasTextAnswer ? "1" : string.Empty,
                hasTextAnswer ? answerText : string.Empty,
                "NEXT",
                string.Empty,
                context));
            return;
        }

        var optionIndex = 1;
        foreach (var option in question.Options.OrderBy(option => option.Order).ThenBy(option => option.Id))
        {
            var optionKey = $"{questionKey}_A{optionIndex}";
            var isSelected = context.SelectedOptionIds.Contains(option.Id);
            var skipToQuestionKey = option.SkipToQuestionId.HasValue &&
                                    questionKeys.TryGetValue(option.SkipToQuestionId.Value, out var skipKey)
                ? skipKey
                : string.Empty;

            rows.Add(ResultRow(
                order,
                parentOptionKey,
                questionKey,
                question.Text,
                question.Type,
                question.TextValidation,
                optionKey,
                option.Text,
                isSelected ? "1" : string.Empty,
                string.Empty,
                option.SkipAction,
                skipToQuestionKey,
                context));

            var subIndex = 1;
            foreach (var subQuestion in option.SubQuestions.OrderBy(sub => sub.Order).ThenBy(sub => sub.Id))
            {
                AppendResultQuestionRows(
                    subQuestion,
                    subIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    optionKey,
                    questionKeys,
                    context,
                    rows);
                subIndex++;
            }

            optionIndex++;
        }
    }

    private static IReadOnlyDictionary<string, string?> ResultRow(
        string order,
        string parentOptionKey,
        string questionKey,
        string questionText,
        string type,
        string textValidation,
        string optionKey,
        string optionText,
        string selectedAnswer,
        string? answerText,
        string skipAction,
        string skipToQuestionKey,
        SphinxResultContext context)
    {
        return new Dictionary<string, string?>
        {
            ["Order"] = order,
            ["ParentOptionKey"] = parentOptionKey,
            ["QuestionKey"] = questionKey,
            ["QuestionText"] = questionText,
            ["Type"] = type,
            ["TextValidation"] = textValidation,
            ["OptionKey"] = optionKey,
            ["OptionText"] = optionText,
            ["SelectedAnswer"] = selectedAnswer,
            ["AnswerText"] = answerText ?? string.Empty,
            ["SkipAction"] = skipAction,
            ["SkipToQuestionKey"] = skipToQuestionKey,
            ["Respondent"] = context.Respondent,
            ["AnsweredAt"] = context.AnsweredAt
        };
    }

    private static string BuildResultsFileName(int id, string title)
    {
        var safeTitle = new string(title.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = "survey";
        }

        return $"sphinx_{id}_{safeTitle}_results.xlsx";
    }

    private static string GetAnonymousRespondentLabel(
        Dictionary<string, string> labels,
        string respondent)
    {
        if (!labels.TryGetValue(respondent, out var label))
        {
            label = $"Respondentas {labels.Count + 1}";
            labels[respondent] = label;
        }

        return label;
    }

    private async Task ValidateTextAnswersAsync(
        SqlConnection conn,
        SqlTransaction tx,
        int surveyId,
        IReadOnlyList<SphinxAnswerJson> answers,
        CancellationToken cancellationToken)
    {
        var textAnswers = answers
            .Where(answer => !string.IsNullOrWhiteSpace(answer.Text))
            .ToArray();

        if (textAnswers.Length == 0)
        {
            return;
        }

        var validations = new Dictionary<int, string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                SELECT ID, TEXT_VALIDATION
                FROM {QuestionTable}
                WHERE SURVEY_ID = @SurveyId
                  AND QUESTION_TYPE = N'TEXT';
                """;
            cmd.Parameters.Add("@SurveyId", SqlDbType.Int).Value = surveyId;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                validations[reader.GetInt32(0)] = reader.GetString(1);
            }
        }

        foreach (var answer in textAnswers)
        {
            if (!validations.TryGetValue(answer.QuestionId, out var validation) ||
                validation.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = answer.Text?.Trim() ?? string.Empty;
            if (validation.Equals("EMAIL", StringComparison.OrdinalIgnoreCase) && !IsValidEmail(value))
            {
                throw new InvalidOperationException("El. pasto atsakymas turi atitikti formata tekstas@tekstas.lt.");
            }

            if (validation.Equals("PHONE", StringComparison.OrdinalIgnoreCase) && !IsValidPhone(value))
            {
                throw new InvalidOperationException("Telefono atsakymas turi atitikti formata +37060000000 arba +370-60000000.");
            }

            if (validation.Equals("NUMBER", StringComparison.OrdinalIgnoreCase) && !IsValidNumber(value))
            {
                throw new InvalidOperationException("Skaiciaus atsakymas turi buti skaicius.");
            }
        }
    }

    private static bool IsValidEmail(string value)
    {
        return Regex.IsMatch(
            value,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    private static bool IsValidPhone(string value)
    {
        return Regex.IsMatch(
            value,
            @"^\+370-?\d{8}$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    private static bool IsValidNumber(string value)
    {
        return decimal.TryParse(
            value.Replace(',', '.'),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
    }

    // ─── Flattening helpers ──────────────────────────────────────────────────

    private static string NormalizeTextValidation(string? validation)
    {
        var normalized = validation?.Trim().ToUpperInvariant();
        return normalized is "EMAIL" or "PHONE" or "NUMBER" ? normalized : "NONE";
    }

    private static string NormalizeSurveyKind(string? kind)
    {
        var normalized = kind?.Trim().ToUpperInvariant();
        return normalized is "POLL" ? "POLL" : "SURVEY";
    }

    private static void ValidateDefinitionForKind(
        IReadOnlyList<SphinxQuestionJson> questions,
        string surveyKind)
    {
        if (!surveyKind.Equals("POLL", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (questions.Count != 1)
        {
            throw new InvalidOperationException("Greitas balsavimas turi tureti tik viena klausima.");
        }

        var question = questions[0];
        if (!question.Type.Equals("SINGLE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Greitas balsavimas turi buti vieno pasirinkimo klausimas.");
        }

        var validOptions = question.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .ToArray();
        if (validOptions.Length < 2)
        {
            throw new InvalidOperationException("Greitas balsavimas turi tureti bent du atsakymu variantus.");
        }

        if (validOptions.Any(option => option.SubQuestions.Any(sub => !string.IsNullOrWhiteSpace(sub.Text))))
        {
            throw new InvalidOperationException("Greitas balsavimas negali tureti subklausimu.");
        }
    }

    private sealed record FlatQ(string ClientId, string Text, string Type, string TextValidation, int Order, string? ParentOptionClientId);
    private sealed record FlatO(string ClientId, string Text, int Order, string QuestionClientId, string SkipAction, string? SkipToClientId);
    private sealed record SphinxResultResponse(int Id, string Respondent, DateTimeOffset AnsweredAt);
    private sealed record SphinxResultContext(
        int ResponseId,
        string Respondent,
        string AnsweredAt,
        IReadOnlySet<int> SelectedOptionIds,
        IReadOnlyDictionary<int, string> TextAnswers);

    private static void Flatten(
        SphinxQuestionJson q, int order, string? parentOptClientId,
        List<FlatQ> questions, List<FlatO> options)
    {
        if (string.IsNullOrWhiteSpace(q.Text)) return;

        questions.Add(new FlatQ(
            q.ClientId,
            q.Text.Trim(),
            q.Type,
            NormalizeTextValidation(q.TextValidation),
            order,
            parentOptClientId));

        for (var i = 0; i < q.Options.Count; i++)
        {
            var opt = q.Options[i];
            if (string.IsNullOrWhiteSpace(opt.Text)) continue;

            options.Add(new FlatO(opt.ClientId, opt.Text.Trim(), i, q.ClientId, opt.SkipAction, opt.SkipToId));

            var subOrder = 0;
            foreach (var sub in opt.SubQuestions)
                Flatten(sub, subOrder++, opt.ClientId, questions, options);
        }
    }
}
