namespace DATA_EXCHANGE_HUB.Models;

public sealed record SeshatAccessState(
    string UserName,
    bool IsAdmin,
    IReadOnlySet<string> ViewCountries,
    IReadOnlySet<string> ContributeCountries)
{
    public bool CanOpen => IsAdmin || ViewCountries.Count > 0 || ContributeCountries.Count > 0;

    public bool CanCreate => IsAdmin || ContributeCountries.Count > 0;

    public string RoleLabel => IsAdmin
        ? "ADMIN"
        : ContributeCountries.Count > 0
            ? "CONTRIBUTE"
            : ViewCountries.Count > 0 ? "VIEW" : "NO ACCESS";

    public bool CanViewAny(IEnumerable<string> countries)
    {
        return IsAdmin || countries.Any(country =>
            ViewCountries.Contains(country) || ContributeCountries.Contains(country));
    }

    public bool CanContributeAny(IEnumerable<string> countries)
    {
        return IsAdmin || countries.Any(ContributeCountries.Contains);
    }
}

public sealed record SeshatDocumentSummary(
    int Id,
    string Title,
    string Description,
    string Keywords,
    string Status,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    IReadOnlyList<string> Countries,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string UpdatedBy,
    DateTimeOffset UpdateTime,
    int FileCount,
    int? ReminderDaysBefore)
{
    public bool IsActive => Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);
}

public sealed record SeshatDocumentDetails(
    SeshatDocumentSummary Document,
    IReadOnlyList<SeshatFileEntry> Files,
    IReadOnlyList<SeshatSectionEntry> Sections,
    IReadOnlyList<SeshatReminderEntry> Reminders,
    IReadOnlyList<SeshatLogEntry> Logs)
{
    public IEnumerable<SeshatFileEntry> AgreementFiles => Files.Where(file => !file.IsDeleted && !file.IsMailFile && file.SectionId is null);

    public IEnumerable<SeshatFileEntry> MailFiles => Files.Where(file => !file.IsDeleted && file.IsMailFile && file.SectionId is null);
}

public sealed record SeshatSectionEntry(
    int Id,
    int DocumentId,
    string Title,
    string Body,
    int SortOrder,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SeshatFileEntry> Files);

public sealed record SeshatFileEntry(
    int Id,
    int DocumentId,
    string OriginalFileName,
    int? SectionId,
    string StoredFileName,
    string ContentType,
    long FileSizeBytes,
    string FileKind,
    string Description,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    bool IsDeleted,
    string? DeletedBy,
    DateTimeOffset? DeletedAt,
    string? DeleteReason)
{
    public bool IsMailFile => FileKind is "OUTLOOK_MSG" or "OUTLOOK_EML";
}

public sealed record SeshatReminderEntry(
    int Id,
    int DocumentId,
    string KpiName,
    string NotifyChannel,
    string Email,
    string PhoneNo,
    int DaysBefore,
    string Subject,
    bool Active,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string UpdatedBy,
    DateTimeOffset UpdateTime);
public sealed record SeshatLogEntry(
    long LogId,
    int? DocumentId,
    int? FileId,
    DateTimeOffset EventAt,
    string ActionType,
    string ActorUser,
    string Summary);
