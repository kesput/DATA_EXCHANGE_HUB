namespace DATA_EXCHANGE_HUB.Models;

public sealed record UserExchangeAccessResult(
    UserAccessResult UserAccess,
    IReadOnlyList<ExchangeTableAccess> Tables);
