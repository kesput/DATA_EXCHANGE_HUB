using Microsoft.AspNetCore.Mvc.RazorPages;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;

namespace DATA_EXCHANGE_HUB.Pages.Hub;

public class IndexModel(
    IHubApplicationCatalog applicationCatalog,
    IHubApplicationAcronymService acronymService,
    IHubModuleAccess moduleAccess) : PageModel
{
    public IReadOnlyList<HubApplication> Applications { get; private set; } = [];
    public IReadOnlyList<HubApplication> FilteredApplications { get; private set; } = [];
    public IReadOnlyDictionary<string, HubApplicationAcronym> AcronymByTitle { get; private set; } =
        new Dictionary<string, HubApplicationAcronym>(StringComparer.OrdinalIgnoreCase);
    [BindProperty(SupportsGet = true)] public string SearchTerm { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        AcronymByTitle = acronymService.GetAcronymsByTitle();
        var allowed = new List<HubApplication>();
        foreach (var application in applicationCatalog.GetApplications())
            if (await moduleAccess.CanOpenAsync(application.Page, User, HttpContext.RequestAborted))
                allowed.Add(application);
        Applications = allowed;
        FilteredApplications = string.IsNullOrWhiteSpace(SearchTerm) ? Applications :
            Applications.Where(application =>
                application.Title.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                application.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                application.Status.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}