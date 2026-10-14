using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HubApplicationAcronymService(IWebHostEnvironment environment) : IHubApplicationAcronymService
{
    private readonly Lazy<IReadOnlyDictionary<string, HubApplicationAcronym>> _acronymsByTitle = new(() =>
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "Hub", "application-acronyms.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, HubApplicationAcronym>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(path);
        var acronyms = JsonSerializer.Deserialize<List<HubApplicationAcronym>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        return acronyms
            .Where(acronym => !string.IsNullOrWhiteSpace(acronym.Title))
            .GroupBy(acronym => acronym.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    });

    public IReadOnlyDictionary<string, HubApplicationAcronym> GetAcronymsByTitle() => _acronymsByTitle.Value;
}

