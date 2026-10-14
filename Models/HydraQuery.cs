namespace DATA_EXCHANGE_HUB.Models;

public sealed record HydraQueryDefinition(
    string Id,
    string Name,
    string ConnectionName,
    string Area,
    string RelativePath,
    IReadOnlyList<HydraFilterDefinition> Filters);

public sealed record HydraFilterDefinition(
    string Name,
    string Expression,
    bool IsRequired,
    string DataType,
    IReadOnlyList<string> AllowedOperators,
    string Description = "");

public sealed record HydraQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);


