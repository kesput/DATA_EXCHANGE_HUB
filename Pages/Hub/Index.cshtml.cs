using Microsoft.AspNetCore.Mvc.RazorPages;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;

namespace DATA_EXCHANGE_HUB.Pages.Hub;

public class IndexModel(
    IHubApplicationCatalog applicationCatalog,
    IHubApplicationAcronymService acronymService,
    IOraculService oraculService,
    IKairosService kairosService,
IAtlasService atlasService,
    IFileUploadAccessService fileUploadAccessService,
    ISendMailFileService sendMailFileService,
    IHydraAccessService hydraAccessService,
    IDikeAccessService dikeAccessService,
    IAreteAccessService areteAccessService,
    IArgusAccessService argusAccessService,
    IThemisService themisService) : PageModel
{
    public IReadOnlyList<HubApplication> Applications { get; private set; } = [];

    public IReadOnlyList<HubApplication> FilteredApplications { get; private set; } = [];

    public IReadOnlyDictionary<string, HubApplicationAcronym> AcronymByTitle { get; private set; } =
        new Dictionary<string, HubApplicationAcronym>(StringComparer.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)]
    public string SearchTerm { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        AcronymByTitle = acronymService.GetAcronymsByTitle();
        var applications = applicationCatalog.GetApplications();
        if (!await CanOpenOraculAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("ORACUL", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenKairosAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("KAIROS", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenFileUploadAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("File upload", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

if (!await CanOpenAtlasAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("ATLAS", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenSendMailFileAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("Send Mail File", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenHydraAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("HYDRA", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenDikeAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("DIKE", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenAreteAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("ARETE", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenArgusAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("ARGUS", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!await CanOpenThemisAsync())
        {
            applications = applications
                .Where(application =>
                    !application.Title.Equals("THEMIS", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        Applications = applications;
        FilteredApplications = string.IsNullOrWhiteSpace(SearchTerm)
            ? Applications
            : Applications
                .Where(application =>
                    application.Title.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                    application.Description.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                    application.Status.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private async Task<bool> CanOpenOraculAsync()
    {
        try
        {
            return await oraculService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenKairosAsync()
    {
        try
        {
            return await kairosService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenFileUploadAsync()
    {
        try
        {
            return await fileUploadAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenAtlasAsync()
    {
        try
        {
            return await atlasService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenSendMailFileAsync()
    {
        try
        {
            return await sendMailFileService.HasAnyAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenThemisAsync()
    {
        try
        {
            return await themisService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenHydraAsync()
    {
        try
        {
            return await hydraAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenDikeAsync()
    {
        try
        {
            return await dikeAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenAreteAsync()
    {
        try
        {
            return await areteAccessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanOpenArgusAsync()
    {
        try
        {
            return (await argusAccessService.GetAccessAsync(
                User,
                HttpContext.RequestAborted)).CanOpen;
        }
        catch
        {
            return false;
        }
    }
}








