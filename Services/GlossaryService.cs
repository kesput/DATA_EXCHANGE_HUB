using System.Globalization;
using System.Text.Json;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class GlossaryService(IWebHostEnvironment environment) : IGlossaryService
{
    private const string GlossaryPath = "Data/Glossary/terms.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<GlossaryTermView>> ListAsync(
        CultureInfo culture,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(
            environment.ContentRootPath,
            GlossaryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(fullPath);
        var terms = await JsonSerializer.DeserializeAsync<List<GlossaryTerm>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];
        var isEnglish = culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);

        return terms
            .Where(term => !string.IsNullOrWhiteSpace(term.Term))
            .Select(term =>
            {
                var title = isEnglish ? term.TitleEn : term.TitleLt;
                var description = isEnglish ? term.DescriptionEn : term.DescriptionLt;
                var synonyms = term.Synonyms
                    .Where(synonym => !string.IsNullOrWhiteSpace(synonym))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var searchText = string.Join(
                    ' ',
                    term.Term,
                    term.TitleLt,
                    term.TitleEn,
                    term.DescriptionLt,
                    term.DescriptionEn,
                    string.Join(' ', synonyms));

                return new GlossaryTermView(
                    term.Term,
                    title,
                    description,
                    synonyms,
                    searchText);
            })
            .OrderBy(term => term.Term, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
