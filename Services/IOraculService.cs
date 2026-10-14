using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IOraculService
{
    Task<IReadOnlyList<string>> GetReportsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    Task<OraculQueryResult> ExecuteAsync(
        int? dayNumber,
        DateTime? selectedDate,
        string reportName,
        CancellationToken cancellationToken = default);
}
