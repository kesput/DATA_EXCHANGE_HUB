using System.Text.Json.Serialization;

namespace DATA_EXCHANGE_HUB.Models;

// ─── Domain records ──────────────────────────────────────────────────────────

public sealed record SphinxSurvey(
    int Id,
    string Title,
    string? Description,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string Status,
    int? MaxResponses,
    DateTimeOffset? ExpiresAt,
    bool IsAnonymous,
    string SurveyKind);

public sealed record SphinxSurveyQuestion(
    int Id,
    int SurveyId,
    int? ParentOptionId,
    string Text,
    string Type,
    int Order);

public sealed record SphinxSurveyOption(
    int Id,
    int QuestionId,
    string Text,
    int Order,
    string SkipAction,
    int? SkipToQuestionId);

// ─── Card (listing) ──────────────────────────────────────────────────────────

public sealed record SphinxSurveyCard(
    int Id,
    string Title,
    string? Description,
    string Status,
    int QuestionCount,
    int ResponseCount,
    bool HasResponded,
    int? MaxResponses,
    DateTimeOffset? ExpiresAt,
    bool IsAnonymous,
    string SurveyKind)
{
    public bool IsActive =>
        Status.Equals("OPEN", StringComparison.OrdinalIgnoreCase) &&
        (ExpiresAt is null || ExpiresAt.Value > DateTimeOffset.UtcNow) &&
        (MaxResponses is null || ResponseCount < MaxResponses.Value);
}

// ─── Question / Option tree nodes (for filling form) ─────────────────────────

public sealed class SphinxQuestionNode
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "TEXT";
    public string TextValidation { get; set; } = "NONE";
    public int Order { get; set; }
    public int? ParentOptionId { get; set; }
    public List<SphinxOptionNode> Options { get; set; } = [];
}

public sealed class SphinxOptionNode
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
    public string SkipAction { get; set; } = "NEXT";
    public int? SkipToQuestionId { get; set; }
    public List<SphinxQuestionNode> SubQuestions { get; set; } = [];
}

// ─── Survey detail (filling page) ────────────────────────────────────────────

public sealed record SphinxSurveyDetail(
    SphinxSurvey Survey,
    int ResponseCount,
    bool HasResponded,
    IReadOnlyList<SphinxQuestionNode> TopLevelQuestions)
{
    public bool IsActive =>
        Survey.Status.Equals("OPEN", StringComparison.OrdinalIgnoreCase) &&
        (Survey.ExpiresAt is null || Survey.ExpiresAt.Value > DateTimeOffset.UtcNow) &&
        (Survey.MaxResponses is null || ResponseCount < Survey.MaxResponses.Value || HasResponded);
}

// ─── Management console item ──────────────────────────────────────────────────

public sealed record SphinxManagementItem(
    int Id,
    string Title,
    string Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    int QuestionCount,
    int ResponseCount,
    int? MaxResponses,
    DateTimeOffset? ExpiresAt,
    string SurveyKind);

// ─── Create/Submit requests ───────────────────────────────────────────────────

public sealed record SphinxCreateSurveyRequest(
    string Title,
    string? Description,
    string CreatedBy,
    string DefinitionJson,
    int? MaxResponses,
    DateTimeOffset? ExpiresAt,
    bool IsAnonymous,
    string SurveyKind);

public sealed record SphinxQuickPollResult(
    int OptionId,
    int VoteCount);

public sealed record SphinxSubmitRequest(
    int SurveyId,
    string Respondent,
    string AnswersJson);

public sealed record SphinxResultsExport(
    string FileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

// ─── JSON builder models (deserialized from frontend survey builder) ──────────

public sealed class SphinxDefinitionJson
{
    [JsonPropertyName("questions")]
    public IList<SphinxQuestionJson> Questions { get; set; } = [];
}

public sealed class SphinxQuestionJson
{
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "TEXT";

    [JsonPropertyName("textValidation")]
    public string TextValidation { get; set; } = "NONE";

    [JsonPropertyName("options")]
    public IList<SphinxOptionJson> Options { get; set; } = [];
}

public sealed class SphinxOptionJson
{
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("skipAction")]
    public string SkipAction { get; set; } = "NEXT";

    [JsonPropertyName("skipToId")]
    public string? SkipToId { get; set; }

    [JsonPropertyName("subQuestions")]
    public IList<SphinxQuestionJson> SubQuestions { get; set; } = [];
}

// ─── JSON models (deserialized from frontend filling form) ────────────────────

public sealed class SphinxAnswerJson
{
    [JsonPropertyName("questionId")]
    public int QuestionId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("optionIds")]
    public IList<int> OptionIds { get; set; } = [];
}
