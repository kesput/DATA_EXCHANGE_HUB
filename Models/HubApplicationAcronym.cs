namespace DATA_EXCHANGE_HUB.Models;

public sealed record HubApplicationAcronym
{
    public string Title { get; init; } = string.Empty;
    public string SymbolLt { get; init; } = string.Empty;
    public string SymbolEn { get; init; } = string.Empty;
    public string MeaningLt { get; init; } = string.Empty;
    public string MeaningEn { get; init; } = string.Empty;
    public string TooltipLt { get; init; } = string.Empty;
    public string TooltipEn { get; init; } = string.Empty;
    public IReadOnlyList<HubApplicationAcronymTerm> Terms { get; init; } = [];
}

public sealed record HubApplicationAcronymTerm
{
    public string Letter { get; init; } = string.Empty;
    public string Lt { get; init; } = string.Empty;
    public string LtDescription { get; init; } = string.Empty;
    public string En { get; init; } = string.Empty;
    public string EnDescription { get; init; } = string.Empty;
}
