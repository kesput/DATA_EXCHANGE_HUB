namespace DATA_EXCHANGE_HUB.Models;

public sealed record ArgusServerMetricsDashboard(
    ArgusServerMetricSample? LatestServerMetric,
    IReadOnlyList<ArgusProcessMetricSample> TopCpuProcesses,
    IReadOnlyList<ArgusProcessMetricSample> TopMemoryProcesses,
    IReadOnlyList<ArgusProcessMetricSample> QvProcesses,
    IReadOnlyList<ArgusServerMetricHistorySample> HistorySamples)
{
    public bool HasData => LatestServerMetric is not null;
}

public sealed record ArgusServerMetricSample(
    long ServerMetricId,
    DateTime Timestamp,
    string ServerName,
    decimal? CpuPercent,
    decimal? MemoryUsedPercent,
    decimal? MemoryUsedGb,
    decimal? MemoryAvailableGb,
    string DiskName,
    decimal? DiskUsedPercent,
    decimal? DiskFreeGb,
    decimal? DiskReadMbSec,
    decimal? DiskWriteMbSec,
    decimal? NetworkInMbps,
    decimal? NetworkOutMbps,
    long? UptimeMinutes,
    string? TopCpuProcessesJson,
    string? TopMemoryProcessesJson);

public sealed record ArgusServerMetricHistorySample(
    DateTime Timestamp,
    decimal? CpuPercent,
    decimal? MemoryUsedPercent);
public sealed record ArgusProcessMetricSample(
    long ProcessMetricId,
    DateTime Timestamp,
    string ServerName,
    string ProcessName,
    int? Pid,
    decimal? CpuPercent,
    decimal? MemoryMb,
    DateTime? StartTime,
    long? RuntimeSeconds,
    string CommandLine);



