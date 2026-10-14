using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface ISendMailFileService
{
    Task<IReadOnlyList<SendMailFileCountryAccess>> GetAccessibleCountriesAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    Task<FileInfo?> GetDownloadFileAsync(
        ClaimsPrincipal user,
        string country,
        string fileName,
        CancellationToken cancellationToken = default);
}
