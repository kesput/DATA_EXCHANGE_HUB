using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages;

public sealed class CultureModel : PageModel
{
    private static readonly Dictionary<string, string> SupportedCultures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lt"] = "lt-LT",
        ["en"] = "en-GB"
    };

    public IActionResult OnGet(string culture, string? returnUrl = null)
    {
        var selectedCulture = SupportedCultures.GetValueOrDefault(culture, "lt-LT");
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(selectedCulture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });

        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
        {
            returnUrl = Url.Page("/Index") ?? "/";
        }

        return LocalRedirect(returnUrl);
    }
}
