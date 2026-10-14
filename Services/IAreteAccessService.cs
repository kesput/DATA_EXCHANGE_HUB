using System.Security.Claims;

namespace DATA_EXCHANGE_HUB.Services;

public interface IAreteAccessService
{
    Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
