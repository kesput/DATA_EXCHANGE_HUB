namespace DATA_EXCHANGE_HUB.Services;

public interface IArgusTaskKillService
{
    Task<IReadOnlySet<int>> GetQueuedPidsAsync(
        bool useLocalPath,
        CancellationToken cancellationToken = default);

    Task<string> QueueKillCommandAsync(
        int pid,
        bool useLocalPath,
        CancellationToken cancellationToken = default);

    Task<(string Path, bool Removed)> AbortKillCommandAsync(
        int pid,
        bool useLocalPath,
        CancellationToken cancellationToken = default);
}
