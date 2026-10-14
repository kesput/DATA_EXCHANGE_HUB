using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.SendMailFile;

public sealed class IndexModel(ISendMailFileService sendMailFileService) : PageModel
{
    private const string CountriesSessionKey = "SEND_MAIL_FILE_COUNTRIES";

    public IReadOnlyList<SendMailFileCountryAccess> Countries { get; private set; } = [];

    public bool CanOpen => Countries.Count > 0;

    public async Task OnGetAsync(bool languageOnly = false)
    {
        if (languageOnly && TryLoadCountriesFromSession(out var cachedCountries))
        {
            Countries = cachedCountries;
            return;
        }

        Countries = await sendMailFileService.GetAccessibleCountriesAsync(
            User,
            HttpContext.RequestAborted);
        SaveCountriesToSession(Countries);
    }

    public async Task<IActionResult> OnGetDownloadAsync(string country, string fileName)
    {
        var fileInfo = await sendMailFileService.GetDownloadFileAsync(
            User,
            country,
            fileName,
            HttpContext.RequestAborted);
        if (fileInfo is null)
        {
            return NotFound();
        }

        return PhysicalFile(
            fileInfo.FullName,
            "application/octet-stream",
            fileInfo.Name);
    }

    public string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.##} MB";
        }

        return bytes >= 1024L
            ? $"{bytes / 1024d:0.##} KB"
            : $"{bytes} B";
    }

    private bool TryLoadCountriesFromSession(out IReadOnlyList<SendMailFileCountryAccess> countries)
    {
        countries = [];
        var json = HttpContext.Session.GetString(CountriesSessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            countries = JsonSerializer.Deserialize<IReadOnlyList<SendMailFileCountryAccess>>(json) ?? [];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void SaveCountriesToSession(IReadOnlyList<SendMailFileCountryAccess> countries)
    {
        HttpContext.Session.SetString(
            CountriesSessionKey,
            JsonSerializer.Serialize(countries));
    }
}