using System.Security.Claims;

namespace DATA_EXCHANGE_HUB.Services;

public interface IDikeAccessService
{
    Task<bool> IsAdminAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
