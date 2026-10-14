namespace DATA_EXCHANGE_HUB.Models;

public sealed record KairosAnalysisQueryDefinition(
    string Id,
    string Name,
    string ConnectionName,
    string Area,
    string RelativePath,
    string Description,
    IReadOnlyList<KairosAnalysisFilterDefinition> Filters);

public sealed record KairosAnalysisFilterDefinition(
    string Name,
    string Expression,
    bool IsRequired,
    string DataType,
    IReadOnlyList<string> AllowedOperators,
    bool IsQueryParameter = false,
    string? OutputFormat = null,
    string Description = "");

public sealed record KairosAnalysisQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);


