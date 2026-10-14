using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Pages.Dike;

public sealed class IndexModel(
    IDikeAccessService dikeAccessService,
    IDikeService dikeService) : PageModel
{
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string SearchTerm { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public long? SelectedOwnerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ActivePane { get; set; } = "agreements";

    [BindProperty(SupportsGet = true)]
    public string AgreementPane { get; set; } = "active";

    public IReadOnlyList<DikeClientInfo> Clients { get; private set; } = [];

    public DikeClientInfo? SelectedClient { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanOpenAsync())
        {
            ErrorMessage = "Siam Windows vartotojui DIKE aplikacijos teise nesuteikta.";
            return Page();
        }

        await LoadClientInfoAsync();
        return Page();
    }

    private async Task LoadClientInfoAsync()
    {
        try
        {
            Clients = await dikeService.ListClientsAsync(
                SearchTerm,
                HttpContext.RequestAborted);

            if (SelectedOwnerId.HasValue)
            {
                SelectedClient = await dikeService.GetClientAsync(
                    SelectedOwnerId.Value,
                    HttpContext.RequestAborted);
            }

            if (SelectedClient is null && Clients.Count == 1 && !string.IsNullOrWhiteSpace(SearchTerm))
            {
                SelectedClient = Clients[0];
                SelectedOwnerId = SelectedClient.OwnerId;
            }

            if (SelectedClient is null && Clients.Count > 0 && ActivePane.Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                SelectedClient = Clients[0];
                SelectedOwnerId = SelectedClient.OwnerId;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Clients = [];
            SelectedClient = null;
        }
    }

    private async Task<bool> CanOpenAsync()
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
}
