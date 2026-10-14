using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IHydraQueryService
{
    IReadOnlyList<HydraQueryDefinition> ListQueries();

    IReadOnlyList<HydraQueryDefinition> ListQueries(IReadOnlySet<string> countries);

    Task<HydraQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default);
}
