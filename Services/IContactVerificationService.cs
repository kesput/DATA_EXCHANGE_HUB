using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IContactVerificationService
{
    Task SendCodeAsync(
        ContactVerificationRequest request,
        CancellationToken cancellationToken = default);

    bool IsConfirmed(
        string purpose,
        string channel,
        string contact,
        string context,
        string enteredCode);

    bool HasPending(
        string purpose,
        string channel,
        string contact,
        string context);

    void Clear(string purpose);
}
