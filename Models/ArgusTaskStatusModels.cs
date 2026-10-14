namespace DATA_EXCHANGE_HUB.Models;

public sealed record ArgusTaskStatusDashboard(
    DateTime Date,
    string RootPath,
    bool RootExists,
    IReadOnlyList<ArgusTaskStatusEntry> Entries,
    IReadOnlyList<ArgusTaskStatusDay> Days);

public sealed record ArgusTaskStatusEntry(
    string TaskKey,
    string TriggerName,
    DateTime? StartTime,
    DateTime? RunningTime,
    DateTime? FinishTime,
    string StartFile,
    string RunningFile,
    string FinishFile)
{
    public string Status => RunningTime.HasValue
        ? "RUNING"
        : FinishTime.HasValue ? "FINISH" : "START";

    public bool IsFinished => FinishTime.HasValue && !RunningTime.HasValue;

    public TimeSpan? Duration
    {
        get
        {
            if (!StartTime.HasValue)
            {
                return null;
            }

            return (IsFinished ? FinishTime : DateTime.Now)!.Value - StartTime.Value;
        }
    }
}

public sealed record ArgusTaskStatusDay(
    DateTime Date,
    int Started,
    int Finished,
    int Running);



