using System.Globalization;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed partial class ArgusTaskStatusService(
    IOptions<ArgusOptions> options,
    IWebHostEnvironment environment) : IArgusTaskStatusService
{
    private static readonly string[] StatusFolders = ["START", "RUNING", "FINISH"];
    private readonly ArgusOptions _options = options.Value;

    public async Task<ArgusTaskStatusDashboard> GetDashboardAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var selectedDate = date.Date;
        var rootPath = GetRootPath();
        var rootExists = Directory.Exists(rootPath);
        if (!rootExists)
        {
            return new ArgusTaskStatusDashboard(selectedDate, rootPath, false, [], []);
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TaskStatusReadTimeoutSeconds, 1, 60));

        try
        {
            var loadTask = Task.Run(() => LoadDashboard(selectedDate, rootPath, cancellationToken), cancellationToken);
            var completed = await Task.WhenAny(loadTask, Task.Delay(timeout, cancellationToken));
            if (completed != loadTask)
            {
                return new ArgusTaskStatusDashboard(selectedDate, rootPath, true, [], []);
            }

            return await loadTask;
        }
        catch
        {
            return new ArgusTaskStatusDashboard(selectedDate, rootPath, true, [], []);
        }
    }

    private static ArgusTaskStatusDashboard LoadDashboard(
        DateTime selectedDate,
        string rootPath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            return new ArgusTaskStatusDashboard(selectedDate, rootPath, false, [], []);
        }

        var files = LoadFiles(rootPath, selectedDate, cancellationToken);
        var entries = BuildEntries(files)
            .OrderByDescending(entry => entry.StartTime ?? entry.RunningTime ?? entry.FinishTime ?? DateTime.MinValue)
            .ThenBy(entry => entry.TriggerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var days = BuildDays(selectedDate, files);

        return new ArgusTaskStatusDashboard(selectedDate, rootPath, true, entries, days);
    }

    private string GetRootPath()
    {
        return NormalizePath(_options.StoragePath);
    }

    private string NormalizePath(string configured)
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    private static IReadOnlyList<StatusFile> LoadFiles(
        string rootPath,
        DateTime selectedDate,
        CancellationToken cancellationToken)
    {
        var rows = new List<StatusFile>();
        var startDirectory = Path.Combine(rootPath, "START");
        if (!Directory.Exists(startDirectory))
        {
            return rows;
        }

        foreach (var path in EnumerateDateFiles(startDirectory, selectedDate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var fileName = Path.GetFileName(path);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            var eventTime = info.LastWriteTime;
            var triggerName = NormalizeTriggerName(fileNameWithoutExtension, eventTime);
            var taskKey = BuildTaskKey(fileNameWithoutExtension);

            rows.Add(new StatusFile("START", taskKey, triggerName, eventTime, path));
            AddCompanionStatus(rows, rootPath, "RUNING", fileName, taskKey, triggerName);
            AddCompanionStatus(rows, rootPath, "FINISH", fileName, taskKey, triggerName);
        }

        return rows;
    }

    private static void AddCompanionStatus(
        List<StatusFile> rows,
        string rootPath,
        string status,
        string fileName,
        string taskKey,
        string triggerName)
    {
        var path = Path.Combine(rootPath, status, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        rows.Add(new StatusFile(status, taskKey, triggerName, info.LastWriteTime, path));
    }


    private static IReadOnlyList<ArgusTaskStatusEntry> BuildEntries(IReadOnlyList<StatusFile> files)
    {
        return files
            .GroupBy(file => file.TaskKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var start = group.Where(file => file.Status == "START").OrderBy(file => file.EventTime).LastOrDefault();
                var running = group.Where(file => file.Status == "RUNING").OrderBy(file => file.EventTime).LastOrDefault();
                var finish = group.Where(file => file.Status == "FINISH").OrderBy(file => file.EventTime).LastOrDefault();
                var display = start ?? running ?? finish ?? group.First();

                return new ArgusTaskStatusEntry(
                    group.Key,
                    display.TriggerName,
                    start?.EventTime,
                    running?.EventTime,
                    finish?.EventTime,
                    start?.Path ?? string.Empty,
                    running?.Path ?? string.Empty,
                    finish?.Path ?? string.Empty);
            })
            .ToArray();
    }

    private static IReadOnlyList<ArgusTaskStatusDay> BuildDays(
        DateTime selectedDate,
        IReadOnlyList<StatusFile> files)
    {
        var started = files.Count(file => file.Status == "START");
        var finished = files.Count(file => file.Status == "FINISH");

        return
        [
            new ArgusTaskStatusDay(selectedDate.Date, started, finished, started - finished)
        ];
    }

    private static IEnumerable<string> EnumerateDateFiles(string directory, DateTime date)
    {
        foreach (var pattern in BuildDateFilePatterns(date))
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                yield return path;
            }
        }
    }


    private static string[] BuildDateFilePatterns(DateTime date)
    {
        return
        [
            $"{date:yyyy.MM.dd}*.txt",
            $"{date:yyyy-MM-dd}*.txt",
            $"{date:yyyyMMdd}*.txt"
        ];
    }

    private static bool IsForDate(ArgusTaskStatusEntry entry, DateTime date)
    {
        return entry.StartTime?.Date == date
            || entry.RunningTime?.Date == date
            || entry.FinishTime?.Date == date;
    }

    private static string TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTime? TryParseDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = DateTimePattern().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Value.Replace('_', ' ').Replace('.', '-');
        var formats = new[]
        {
            "yyyy-MM-dd HH-mm-ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyyMMdd HHmmss",
            "yyyyMMdd HHmm",
            "yyyyMMdd"
        };

        return DateTime.TryParseExact(
            raw,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)
                ? parsed
                : null;
    }

    private static string NormalizeTriggerName(string fileName, DateTime eventTime)
    {
        var withoutDate = DateTimePrefixPattern().Replace(fileName, string.Empty).Trim('_', '-', ' ');
        return string.IsNullOrWhiteSpace(withoutDate) ? fileName : withoutDate;
    }

    private static string BuildTaskKey(string fileNameWithoutExtension)
    {
        return fileNameWithoutExtension.ToUpperInvariant();
    }

    [GeneratedRegex(@"\d{4}[-.]?\d{2}[-.]?\d{2}(?:[ _T-]?\d{2}[:.\-]?\d{2}(?:[:.\-]?\d{2})?)?")]
    private static partial Regex DateTimePattern();

    [GeneratedRegex(@"^\d{4}[-.]?\d{2}[-.]?\d{2}(?:[ _T-]?\d{2}[:.\-]?\d{2}(?:[:.\-]?\d{2})?)?[_\-\s]*")]
    private static partial Regex DateTimePrefixPattern();

    private sealed record StatusFile(
        string Status,
        string TaskKey,
        string TriggerName,
        DateTime EventTime,
        string Path);
}












