namespace DATA_EXCHANGE_HUB.Models;

public sealed record GlossaryTerm(
    string Term,
    string TitleLt,
    string TitleEn,
    string DescriptionLt,
    string DescriptionEn,
    IReadOnlyList<string> Synonyms);

public sealed record GlossaryTermView(
    string Term,
    string Title,
    string Description,
    IReadOnlyList<string> Synonyms,
    string SearchText);
