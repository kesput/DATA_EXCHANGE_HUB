namespace DATA_EXCHANGE_HUB.Models;

public sealed record ApisQueryDefinition(
    string Id,
    string Name,
    string ConnectionName,
    string Area,
    string RelativePath,
    string CommandType,
    string? ProcedureName,
    string Description,
    IReadOnlyList<ApisParameterDefinition> Parameters,
    string ViewMode = "");

public sealed record ApisParameterDefinition(
    string Name,
    bool IsRequired,
    string DataType,
    string? DefaultValue,
    string Description,
    IReadOnlyList<string> AllowedValues,
    string? Pattern,
    string? PatternMessage,
    IReadOnlyList<string> AllowedOperators,
    string? FilterExpression,
    string? OutputFormat);

public sealed record ApisParameterValue(
    string Name,
    string? Value,
    string Operator = ExchangeTableFilter.EqualsOperator,
    string Connector = ExchangeTableFilter.AndConnector);

public sealed record ApisQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
