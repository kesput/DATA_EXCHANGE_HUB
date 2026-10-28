using Microsoft.AspNetCore.Mvc.RazorPages;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;

namespace DATA_EXCHANGE_HUB.Pages.Hub;

public class IndexModel(
    IHubApplicationCatalog applicationCatalog,
    IHubApplicationAcronymService acronymService,
    IHubModuleAccess moduleAccess,
    IUserNameResolver userNameResolver) : PageModel
{
    public string CurrentUserName { get; private set; } = string.Empty;
    public bool HasAccessCheckErrors { get; private set; }
    public IReadOnlyList<HubApplication> Applications { get; private set; } = [];
    public IReadOnlyList<HubApplication> FilteredApplications { get; private set; } = [];
    public IReadOnlyDictionary<string, HubApplicationAcronym> AcronymByTitle { get; private set; } =
        new Dictionary<string, HubApplicationAcronym>(StringComparer.OrdinalIgnoreCase);
    [BindProperty(SupportsGet = true)] public string SearchTerm { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        CurrentUserName = userNameResolver.ResolveUserName(User);
        if (string.IsNullOrWhiteSpace(CurrentUserName) || HttpContext.Session.GetString(LoginModel.BlockedSessionKey) == "1")
            return RedirectToPage("/Hub/Login", new { ReturnUrl = "/Hub/Index" });

        AcronymByTitle = acronymService.GetAcronymsByTitle();
        var allowed = new List<HubApplication>();
        foreach (var application in applicationCatalog.GetApplications())
            if (await moduleAccess.CanOpenAsync(application.Page, User, HttpContext.RequestAborted))
                allowed.Add(application);
        Applications = allowed;
        HasAccessCheckErrors = moduleAccess.HasCheckFailures;
        if (HasAccessCheckErrors && Applications.Count == 0)
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        FilteredApplications = string.IsNullOrWhiteSpace(SearchTerm) ? Applications :
            Applications.Where(application =>
                application.Title.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                application.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                application.Status.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Page();
    }
}
