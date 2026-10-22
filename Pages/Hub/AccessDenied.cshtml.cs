using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Hub;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccessDeniedModel : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Application { get; set; }
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
