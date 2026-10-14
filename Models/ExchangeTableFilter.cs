namespace DATA_EXCHANGE_HUB.Models;

public sealed record ExchangeTableFilter(
    string ColumnName,
    string Operator,
    string Value,
    string Connector)
{
    public const string EqualsOperator = "eq";
    public const string NotEqualsOperator = "ne";
    public const string LikeOperator = "like";
    public const string InOperator = "in";
    public const string BetweenOperator = "between";
    public const string GreaterThanOperator = "gt";
    public const string GreaterThanOrEqualOperator = "gte";
    public const string LessThanOperator = "lt";
    public const string LessThanOrEqualOperator = "lte";
    public const string AndConnector = "and";
    public const string OrConnector = "or";

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(ColumnName) &&
        !string.IsNullOrWhiteSpace(Value);
}
