using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface ISendSmsService
{
    Task SendAsync(
        SendSmsRequest request,
        CancellationToken cancellationToken = default);
}

