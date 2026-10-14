using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DATA_EXCHANGE_HUB.Pages.Argus;

public sealed class IndexModel(
    IArgusAccessService argusAccessService,
    IArgusTaskStatusService taskStatusService,
    IArgusServerMetricsService serverMetricsService,
    IArgusTaskKillService taskKillService,
    IApisQvdPreviewService qvdPreviewService,
    IWebHostEnvironment environment) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateTime? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? View { get; set; }

    [BindProperty(SupportsGet = true)]
    public string QvdFileId { get; set; } = string.Empty;

    public ArgusAccessState Access { get; private set; } = new(false, false);

    public ArgusTaskStatusDashboard Dashboard { get; private set; } =
        new(DateTime.Today, string.Empty, false, [], []);

    public ArgusServerMetricsDashboard ServerMetrics { get; private set; } = new(null, [], [], [], []);

    public string? ServerMetricsError { get; private set; }

    public IReadOnlyList<ApisQvdRoot> QvdRoots { get; private set; } = [];

    public IReadOnlyList<ApisQvdFile> QvdFiles { get; private set; } = [];

    public ApisQvdPreview? QvdPreview { get; private set; }

    public string? QvdPreviewError { get; private set; }

    public bool IsCalendarView => string.Equals(View, "calendar", StringComparison.OrdinalIgnoreCase);

    public bool IsQvdView => string.Equals(View, "qvd", StringComparison.OrdinalIgnoreCase);

    public bool CanKillPid => Access.IsAdmin || IsLocalRequest();

    public bool CanViewQvdPreview => Access.IsAdmin || IsLocalRequest();

    public IReadOnlySet<int> QueuedKillPids { get; private set; } = new HashSet<int>();

    [TempData]
    public string? TaskKillMessage { get; set; }

    [TempData]
    public string? TaskKillError { get; set; }

    public IReadOnlyList<ArgusMetric> Metrics =>
    [
        new("Vyksta", Dashboard.Entries.Count(entry => !entry.IsFinished).ToString(), "START be FINISH"),
        new("Baigta", Dashboard.Entries.Count(entry => entry.IsFinished).ToString(), "FINISH irasai"),
        new("Viso", Dashboard.Entries.Count.ToString(), "Pasirinktos dienos triggeriai"),
        new("Katalogas", Dashboard.RootExists ? "OK" : "Nera", Dashboard.RootPath)
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        if (IsCalendarView)
        {
            Dashboard = await taskStatusService.GetDashboardAsync(
                (Date ?? DateTime.Today).Date,
                HttpContext.RequestAborted);
        }
        else
        {
            Dashboard = new ArgusTaskStatusDashboard(
                (Date ?? DateTime.Today).Date,
                string.Empty,
                true,
                [],
                []);
        }

        if (!IsCalendarView && !IsQvdView)
        {
            try
            {
                ServerMetrics = await serverMetricsService.GetDashboardAsync(HttpContext.RequestAborted);
                if (CanKillPid)
                {
                    QueuedKillPids = await taskKillService.GetQueuedPidsAsync(
                        UseLocalTaskKillPath(),
                        HttpContext.RequestAborted);
                }
            }
            catch (Exception ex)
            {
                ServerMetricsError = ex.Message;
                ServerMetrics = new ArgusServerMetricsDashboard(null, [], [], [], []);
            }
        }

        if (IsQvdView && CanViewQvdPreview)
        {
            LoadQvdPreview();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostKillPidAsync(int pid)
    {
        Access = await GetAccessAsync();
        if (!Access.CanOpen)
        {
            return Forbid();
        }

        if (!CanKillPid)
        {
            return Forbid();
        }

        if (pid <= 0)
        {
            TaskKillError = "PID nerastas arba neteisingas.";
            return RedirectToPage("/Argus/Index");
        }

        try
        {
            var useLocalPath = UseLocalTaskKillPath();
            var queuedPids = await taskKillService.GetQueuedPidsAsync(
                useLocalPath,
                HttpContext.RequestAborted);
            if (queuedPids.Contains(pid))
            {
                var result = await taskKillService.AbortKillCommandAsync(
                    pid,
                    useLocalPath,
                    HttpContext.RequestAborted);
                TaskKillMessage = result.Removed
                    ? $"PID {pid} kill komanda pasalinta is {result.Path}."
                    : $"PID {pid} kill komandos faile nerasta.";
            }
            else
            {
                var path = await taskKillService.QueueKillCommandAsync(
                    pid,
                    useLocalPath,
                    HttpContext.RequestAborted);
                TaskKillMessage = $"PID {pid} kill komanda irasyta i {path}.";
            }
        }
        catch (Exception ex)
        {
            TaskKillError = $"Nepavyko pakeisti PID {pid} kill komandos: {ex.Message}";
        }

        return RedirectToPage("/Argus/Index");
    }

    public string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "-";
        }

        var value = duration.Value;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours} val. {value.Minutes:00} min."
            : $"{value.Minutes:00} min. {value.Seconds:00} sek.";
    }

    public string FormatSeconds(long? seconds)
    {
        return seconds.HasValue
            ? FormatDuration(TimeSpan.FromSeconds(seconds.Value))
            : "-";
    }
    public string FormatTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("HH:mm:ss") : "-";
    }

    public string FormatDateTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-";
    }

    public string FormatNumber(decimal? value, string suffix = "", int decimals = 1)
    {
        return value.HasValue ? $"{value.Value.ToString($"N{decimals}")}{suffix}" : "-";
    }

    private async Task<ArgusAccessState> GetAccessAsync()
    {
        try
        {
            return await argusAccessService.GetAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return new ArgusAccessState(false, false);
        }
    }

    private void LoadQvdPreview()
    {
        QvdRoots = qvdPreviewService.ListRoots();
        QvdFiles = qvdPreviewService.ListFiles();
        if (QvdFiles.Count == 0)
        {
            QvdPreview = null;
            return;
        }

        try
        {
            QvdPreview = qvdPreviewService.ReadPreview(QvdFileId, 100);
            if (QvdPreview is not null && string.IsNullOrWhiteSpace(QvdFileId))
            {
                QvdFileId = QvdPreview.File.Id;
            }
        }
        catch (Exception exception)
        {
            QvdPreviewError = exception.Message;
            QvdPreview = null;
        }
    }

    private bool IsLocalRequest()
    {
        var connection = HttpContext.Connection;
        if (connection.RemoteIpAddress is null)
        {
            return true;
        }

        if (System.Net.IPAddress.IsLoopback(connection.RemoteIpAddress))
        {
            return true;
        }

        return connection.LocalIpAddress is not null &&
            connection.RemoteIpAddress.Equals(connection.LocalIpAddress);
    }

    private bool UseLocalTaskKillPath()
    {
        return IsLocalRequest() || environment.IsDevelopment();
    }
}

public sealed record ArgusMetric(string Title, string Value, string Hint);









