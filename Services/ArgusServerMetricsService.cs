using System.Data;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ArgusServerMetricsService(
    IConfiguration configuration,
    IOptions<ArgusOptions> options) : IArgusServerMetricsService
{
    private readonly ArgusOptions _options = options.Value;

    public async Task<ArgusServerMetricsDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = GetConnectionString(_options.DataConnectionStringName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var serverMetricsTableName = SqlName.NormalizeObjectName(_options.ServerMetricsTableName, "BLS_LOGIT_STAT.dbo.server_metrics");
        var processMetricsTableName = SqlName.NormalizeObjectName(_options.ProcessMetricsTableName, "BLS_LOGIT_STAT.dbo.process_metrics");
        var commandTimeoutSeconds = Math.Clamp(_options.ServerMetricsCommandTimeoutSeconds, 1, 120);

        var latest = await GetLatestServerMetricAsync(connection, serverMetricsTableName, commandTimeoutSeconds, cancellationToken);
        if (latest is null)
        {
            return new ArgusServerMetricsDashboard(null, [], [], [], []);
        }

        var topCpu = await TryLoadMetricsAsync(() => GetTopProcessesAsync(
            connection,
            processMetricsTableName,
            latest.Timestamp,
            "cpu_percent",
            commandTimeoutSeconds,
            cancellationToken));
        var topMemory = await TryLoadMetricsAsync(() => GetTopProcessesAsync(
            connection,
            processMetricsTableName,
            latest.Timestamp,
            "memory_mb",
            commandTimeoutSeconds,
            cancellationToken));
        var qvProcesses = await TryLoadMetricsAsync(() => GetQvProcessesAsync(
            connection,
            processMetricsTableName,
            latest.Timestamp,
            commandTimeoutSeconds,
            cancellationToken));
        var history = await TryLoadMetricsAsync(() => GetHistorySamplesAsync(
            connection,
            serverMetricsTableName,
            latest.Timestamp,
            commandTimeoutSeconds,
            cancellationToken));

        return new ArgusServerMetricsDashboard(latest, topCpu, topMemory, qvProcesses, history);
    }

    private static async Task<IReadOnlyList<T>> TryLoadMetricsAsync<T>(Func<Task<IReadOnlyList<T>>> load)
    {
        try
        {
            return await load();
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            return [];
        }
    }
    private static async Task<ArgusServerMetricSample?> GetLatestServerMetricAsync(
        SqlConnection connection,
        string serverMetricsTableName,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = $"""
            SELECT TOP (1)
                [server_metric_id],
                [timestamp],
                [server_name],
                [cpu_percent],
                [memory_used_percent],
                [memory_used_gb],
                [memory_available_gb],
                [disk_name],
                [disk_used_percent],
                [disk_free_gb],
                [disk_read_mb_sec],
                [disk_write_mb_sec],
                [network_in_mbps],
                [network_out_mbps],
                [uptime_minutes],
                [top_cpu_processes_json],
                [top_memory_processes_json]
            FROM {serverMetricsTableName}
            ORDER BY [server_metric_id] DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArgusServerMetricSample(
            GetInt64(reader, "server_metric_id"),
            GetDateTime(reader, "timestamp") ?? DateTime.MinValue,
            GetString(reader, "server_name"),
            GetDecimal(reader, "cpu_percent"),
            GetDecimal(reader, "memory_used_percent"),
            GetDecimal(reader, "memory_used_gb"),
            GetDecimal(reader, "memory_available_gb"),
            GetString(reader, "disk_name"),
            GetDecimal(reader, "disk_used_percent"),
            GetDecimal(reader, "disk_free_gb"),
            GetDecimal(reader, "disk_read_mb_sec"),
            GetDecimal(reader, "disk_write_mb_sec"),
            GetDecimal(reader, "network_in_mbps"),
            GetDecimal(reader, "network_out_mbps"),
            GetNullableInt64(reader, "uptime_minutes"),
            GetNullableString(reader, "top_cpu_processes_json"),
            GetNullableString(reader, "top_memory_processes_json"));
    }


    private static async Task<IReadOnlyList<ArgusServerMetricHistorySample>> GetHistorySamplesAsync(
        SqlConnection connection,
        string serverMetricsTableName,
        DateTime latestTimestamp,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = $"""
            WITH samples AS (
                SELECT
                    BucketStart = DATEADD(MINUTE, (DATEDIFF(MINUTE, 0, [timestamp]) / 5) * 5, 0),
                    [cpu_percent],
                    [memory_used_percent]
                FROM {serverMetricsTableName}
                WHERE [timestamp] >= DATEADD(HOUR, -6, @LatestTimestamp)
                  AND [timestamp] <= @LatestTimestamp
            )
            SELECT
                [timestamp] = BucketStart,
                [cpu_percent] = AVG([cpu_percent]),
                [memory_used_percent] = AVG([memory_used_percent])
            FROM samples
            GROUP BY BucketStart
            ORDER BY BucketStart;
            """;
        command.Parameters.Add("@LatestTimestamp", SqlDbType.DateTime2).Value = latestTimestamp;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ArgusServerMetricHistorySample>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ArgusServerMetricHistorySample(
                GetDateTime(reader, "timestamp") ?? DateTime.MinValue,
                GetDecimal(reader, "cpu_percent"),
                GetDecimal(reader, "memory_used_percent")));
        }

        return rows;
    }
    private static async Task<IReadOnlyList<ArgusProcessMetricSample>> GetTopProcessesAsync(
        SqlConnection connection,
        string processMetricsTableName,
        DateTime timestamp,
        string orderColumn,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (orderColumn is not "cpu_percent" and not "memory_mb")
        {
            throw new InvalidOperationException("Netinkamas procesu rikiavimo stulpelis.");
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = $"""
            SELECT TOP (8)
                [process_metric_id],
                [timestamp],
                [server_name],
                [process_name],
                [pid],
                [cpu_percent],
                [memory_mb],
                [command_line],
                [start_time],
                [runtime_seconds]
            FROM {processMetricsTableName}
            WHERE [timestamp] = @Timestamp
            ORDER BY [{orderColumn}] DESC, [process_metric_id] DESC;
            """;
        command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = timestamp;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ArgusProcessMetricSample>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ArgusProcessMetricSample(
                GetInt64(reader, "process_metric_id"),
                GetDateTime(reader, "timestamp") ?? DateTime.MinValue,
                GetString(reader, "server_name"),
                GetString(reader, "process_name"),
                GetNullableInt32(reader, "pid"),
                GetDecimal(reader, "cpu_percent"),
                GetDecimal(reader, "memory_mb"),
                GetDateTime(reader, "start_time"),
                GetNullableInt64(reader, "runtime_seconds"),
                GetString(reader, "command_line")));
        }

        return rows;
    }


    private static async Task<IReadOnlyList<ArgusProcessMetricSample>> GetQvProcessesAsync(
        SqlConnection connection,
        string processMetricsTableName,
        DateTime timestamp,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = $"""
            SELECT TOP (50)
                [process_metric_id],
                [timestamp],
                [server_name],
                [process_name],
                [pid],
                [cpu_percent],
                [memory_mb],
                [command_line],
                [start_time],
                [runtime_seconds]
            FROM {processMetricsTableName}
            WHERE [timestamp] = @Timestamp
                AND (
                    [process_name] LIKE 'QV%'
                )
                AND LOWER(COALESCE([process_name], '')) <> 'qvconnect64'
            ORDER BY [cpu_percent] DESC, [memory_mb] DESC, [process_metric_id] DESC;
            """;
        command.Parameters.Add("@Timestamp", SqlDbType.DateTime2).Value = timestamp;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ArgusProcessMetricSample>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ArgusProcessMetricSample(
                GetInt64(reader, "process_metric_id"),
                GetDateTime(reader, "timestamp") ?? DateTime.MinValue,
                GetString(reader, "server_name"),
                GetString(reader, "process_name"),
                GetNullableInt32(reader, "pid"),
                GetDecimal(reader, "cpu_percent"),
                GetDecimal(reader, "memory_mb"),
                GetDateTime(reader, "start_time"),
                GetNullableInt64(reader, "runtime_seconds"),
                GetString(reader, "command_line")));
        }

        return rows;
    }
    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }

    private static string GetString(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    private static string? GetNullableString(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static DateTime? GetDateTime(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    private static decimal? GetDecimal(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
    }

    private static long GetInt64(SqlDataReader reader, string name)
    {
        return Convert.ToInt64(reader[name]);
    }

    private static long? GetNullableInt64(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }
}

















