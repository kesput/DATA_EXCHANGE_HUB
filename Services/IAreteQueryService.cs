using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IAreteQueryService
{
    IReadOnlyList<AreteQueryDefinition> ListQueries();

    IReadOnlyList<AreteQueryDefinition> ListQueries(IReadOnlySet<string> countries);

    Task<AreteQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default);
}
