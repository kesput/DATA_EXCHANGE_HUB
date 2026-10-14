namespace DATA_EXCHANGE_HUB.Models;

public sealed class ExchangeTableUpdateRequest
{
    public string TableName { get; set; } = string.Empty;

    public Dictionary<string, Dictionary<string, string?>> Updates { get; set; } = [];
}
