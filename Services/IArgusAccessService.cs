using System.Security.Claims;

namespace DATA_EXCHANGE_HUB.Services;

public interface IArgusAccessService
{
    Task<ArgusAccessState> GetAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}

public sealed record ArgusAccessState(bool CanOpen, bool IsAdmin);

