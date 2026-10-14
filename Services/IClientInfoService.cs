using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IClientInfoService
{
    Task<IReadOnlyList<HydraFilterDefinition>> GetFilterDefinitionsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    Task<ClientInfoResult> ExecuteAsync(
        IReadOnlyList<ExchangeTableFilter> filters,
        CancellationToken cancellationToken = default);
}
