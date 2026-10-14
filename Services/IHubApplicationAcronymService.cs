using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IHubApplicationAcronymService
{
    IReadOnlyDictionary<string, HubApplicationAcronym> GetAcronymsByTitle();
}

