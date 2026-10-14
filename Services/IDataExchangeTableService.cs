using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IDataExchangeTableService
{
    Task<ExchangeTableData> GetTableDataAsync(
        ExchangeTableAccess tableAccess,
        int rowLimit,
        int pageNumber,
        bool includeRows = true,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        CancellationToken cancellationToken = default);

    Task<int> CountRowsByFilterAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyList<ExchangeTableFilter> filters,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeLogEntry>> ListExchangeLogsAsync(
        ExchangeTableAccess tableAccess,
        string actorUserName,
        bool canSeeAllLogs,
        int rowLimit = 25,
        CancellationToken cancellationToken = default);

    Task AddRowAsync(
        ExchangeTableAccess tableAccess,
        IFormCollection formData,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task<int> AddRowsAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task UpdateRowsAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyDictionary<string, Dictionary<string, string?>> updates,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task DeleteRowAsync(
        ExchangeTableAccess tableAccess,
        string keyValue,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task<int> DeleteRowsByFilterAsync(
        ExchangeTableAccess tableAccess,
        IReadOnlyList<ExchangeTableFilter> filters,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task RevertLogEntryAsync(
        ExchangeTableAccess tableAccess,
        long logId,
        string actorUserName,
        bool canRevertAnyUser,
        CancellationToken cancellationToken = default);
}
