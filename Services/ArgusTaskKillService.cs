using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ArgusTaskKillService(
    IOptions<ArgusOptions> options,
    IWebHostEnvironment environment) : IArgusTaskKillService
{
    private static readonly TimeSpan FiveMinuteBoundaryDelay = TimeSpan.FromSeconds(5);
    private const int FiveMinuteBoundaryToleranceSeconds = 10;
    private readonly ArgusOptions _options = options.Value;

    public async Task<IReadOnlySet<int>> GetQueuedPidsAsync(
        bool useLocalPath,
        CancellationToken cancellationToken = default)
    {
        var path = GetCommandFilePath(useLocalPath);
        if (!File.Exists(path))
        {
            return new HashSet<int>();
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        return lines
            .Select(TryParsePid)
            .Where(pid => pid.HasValue)
            .Select(pid => pid!.Value)
            .ToHashSet();
    }

    public async Task<string> QueueKillCommandAsync(
        int pid,
        bool useLocalPath,
        CancellationToken cancellationToken = default)
    {
        if (pid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), "PID turi buti teigiamas skaicius.");
        }

        var path = GetCommandFilePath(useLocalPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var command = BuildCommand(pid);
        var queuedPids = await GetQueuedPidsAsync(useLocalPath, cancellationToken);
        if (queuedPids.Contains(pid))
        {
            return path;
        }

        await DelayNearFiveMinuteBoundaryAsync(cancellationToken);
        await File.AppendAllTextAsync(path, command + Environment.NewLine, cancellationToken);

        return path;
    }

    public async Task<(string Path, bool Removed)> AbortKillCommandAsync(
        int pid,
        bool useLocalPath,
        CancellationToken cancellationToken = default)
    {
        if (pid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), "PID turi buti teigiamas skaicius.");
        }

        var path = GetCommandFilePath(useLocalPath);
        if (!File.Exists(path))
        {
            return (path, false);
        }

        var removed = false;
        var expected = BuildCommand(pid);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var remaining = lines
            .Where(line =>
            {
                if (string.Equals(line.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    return false;
                }

                return true;
            })
            .ToArray();

        if (!removed)
        {
            return (path, false);
        }

        await DelayNearFiveMinuteBoundaryAsync(cancellationToken);
        await File.WriteAllLinesAsync(path, remaining, cancellationToken);
        return (path, removed);
    }

    private static async Task DelayNearFiveMinuteBoundaryAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        if (now.Minute % 5 == 0 && now.Second <= FiveMinuteBoundaryToleranceSeconds)
        {
            await Task.Delay(FiveMinuteBoundaryDelay, cancellationToken);
        }
    }

    private string GetCommandFilePath(bool useLocalPath)
    {
        return NormalizePath(useLocalPath
            ? _options.LocalTaskKillCommandFilePath
            : _options.IisTaskKillCommandFilePath);
    }

    private static string BuildCommand(int pid)
    {
        return $"taskkill /F /PID {pid}";
    }

    private static int? TryParsePid(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "taskkill", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[1], "/F", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[2], "/PID", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[3], out var pid))
        {
            return null;
        }

        return pid;
    }

    private string NormalizePath(string configured)
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }
}
