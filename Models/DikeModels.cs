namespace DATA_EXCHANGE_HUB.Models;

public sealed record DikeClientInfo(
    long OwnerId,
    string FirmId,
    string Sid,
    string ShortName,
    string Name,
    string RegCode,
    string Address);
