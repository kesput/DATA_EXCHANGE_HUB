namespace DATA_EXCHANGE_HUB.Models;

public sealed record AreteQueryDefinition(
    string Id,
    string Name,
    string ConnectionName,
    string Area,
    string RelativePath,
    string Description,
    IReadOnlyList<AreteFilterDefinition> Filters);

public sealed record AreteFilterDefinition(
    string Name,
    string Expression,
    bool IsRequired,
    string DataType,
    IReadOnlyList<string> AllowedOperators,
    bool IsQueryParameter = false,
    string? OutputFormat = null,
    string Description = "");

public sealed record AreteQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

