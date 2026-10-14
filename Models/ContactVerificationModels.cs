namespace DATA_EXCHANGE_HUB.Models;

public sealed record ContactVerificationRequest(
    string Purpose,
    string Channel,
    string Contact,
    string Context,
    string CreatedBy);

public sealed record ContactVerificationState(
    string Purpose,
    string Channel,
    string Contact,
    string Context,
    string Code,
    DateTimeOffset ExpiresAt);
