using DATA_EXCHANGE_HUB.Models;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HubApplicationCatalog(IOptions<List<HubApplication>> options) : IHubApplicationCatalog
{
    private readonly IReadOnlyList<HubApplication> _applications = options.Value;

    public IReadOnlyList<HubApplication> GetApplications() => _applications;
}
