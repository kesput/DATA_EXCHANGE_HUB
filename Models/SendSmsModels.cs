namespace DATA_EXCHANGE_HUB.Models;

public sealed record SendSmsRequest(
    string To,
    string Text,
    string CreatedBy,
    string Source);

