using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IKairosAnalysisQueryService
{
    IReadOnlyList<KairosAnalysisQueryDefinition> ListQueries();

    IReadOnlyList<KairosAnalysisQueryDefinition> ListQueries(IReadOnlySet<string> countries);

    Task<KairosAnalysisQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default);
}
