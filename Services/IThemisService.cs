using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IThemisService
{
    Task<bool> HasAccessAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    IReadOnlyList<ExchangeTableAccess> GetConfiguredTables();

    Task<ExchangeTableData> GetTableDataAsync(
        ExchangeTableAccess table,
        int rowLimit,
        int pageNumber,
        IReadOnlyList<ExchangeTableFilter>? filters = null,
        CancellationToken cancellationToken = default);

    Task<int> CountRowsByFilterAsync(
        ExchangeTableAccess table,
        IReadOnlyList<ExchangeTableFilter> filters,
        CancellationToken cancellationToken = default);

    Task AddRowAsync(
        ExchangeTableAccess table,
        IFormCollection formData,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task<int> AddRowsAsync(
        ExchangeTableAccess table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task UpdateRowsAsync(
        ExchangeTableAccess table,
        IReadOnlyDictionary<string, Dictionary<string, string?>> updates,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task DeleteRowAsync(
        ExchangeTableAccess table,
        string keyValue,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task<int> DeleteRowsByFilterAsync(
        ExchangeTableAccess table,
        IReadOnlyList<ExchangeTableFilter> filters,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeLogEntry>> ListLogsAsync(
        ExchangeTableAccess table,
        int rowLimit = 25,
        CancellationToken cancellationToken = default);

    Task RevertLogEntryAsync(
        ExchangeTableAccess table,
        long logId,
        string actorUserName,
        CancellationToken cancellationToken = default);
}
