using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IApisQueryService
{
    IReadOnlyList<ApisQueryDefinition> ListQueries();

    IReadOnlyList<ApisQueryDefinition> ListQueries(IReadOnlySet<string> countries);

    Task<ApisQueryResult> ExecuteAsync(
        string queryId,
        IReadOnlyList<ApisParameterValue>? parameters = null,
        IReadOnlySet<string>? countries = null,
        CancellationToken cancellationToken = default);
}
