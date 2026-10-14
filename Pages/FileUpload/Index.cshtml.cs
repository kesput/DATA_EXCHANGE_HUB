using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using DATA_EXCHANGE_HUB;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Data;

namespace DATA_EXCHANGE_HUB.Pages.FileUpload;

[RequestSizeLimit(110L * 1024L * 1024L)]
[RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024L * 1024L)]
public sealed class IndexModel(
    IOptions<FileUploadOptions> fileUploadOptions,
    IFileUploadAccessService accessService,
    IFileUploadNotificationService notificationService,
    IContactVerificationService contactVerificationService,
    IStringLocalizer<SharedResource> localizer,
    IConfiguration configuration,
    IUserNameResolver userNameResolver) : PageModel
{
    private const string NotificationVerificationPurpose = "FILE_UPLOAD_NOTIFICATION";
    private readonly FileUploadOptions _options = fileUploadOptions.Value;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public string StoragePath => _options.StoragePath;

    public int MaxFileSizeMb => _options.MaxFileSizeMb;

    public int MaxStorageSizeGb => _options.MaxStorageSizeGb;

    public long UsedStorageBytes { get; private set; }

    public int UsedStoragePercent => _options.MaxStorageSizeBytes <= 0
        ? 0
        : (int)Math.Min(100, Math.Ceiling(UsedStorageBytes * 100d / _options.MaxStorageSizeBytes));

    public bool IsStorageLimitReached => UsedStorageBytes >= _options.MaxStorageSizeBytes;

    public bool StorageExists { get; private set; }

    public bool CanOpen { get; private set; }

    public IReadOnlyList<UploadedFileInfo> RecentFiles { get; private set; } = [];

    public IReadOnlyList<FileUploadNotificationSubscription> NotificationSubscriptions { get; private set; } = [];

    [BindProperty]
    public string NotificationPattern { get; set; } = string.Empty;

    [BindProperty]
    public string NotificationTriggerType { get; set; } = "ANY_UPLOAD";

    [BindProperty]
    public string NotificationChannel { get; set; } = "EMAIL";

    [BindProperty]
    public string NotificationContact { get; set; } = string.Empty;

    [BindProperty]
    public string NotificationVerificationCode { get; set; } = string.Empty;

    [BindProperty]
    public bool ResendNotificationVerification { get; set; }

    public async Task OnGetAsync()
    {
        CanOpen = await CanOpenAsync();
        if (!CanOpen)
        {
            return;
        }

        LoadStorageState();
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? uploadFile)
    {
        if (!await CanOpenAsync())
        {
            ErrorMessage = localizer["FileUpload.Error.AccessDenied"];
            return RedirectToPage();
        }

        if (uploadFile is null || uploadFile.Length == 0)
        {
            ErrorMessage = localizer["FileUpload.Error.EmptyFile"];
            return RedirectToPage();
        }

        if (uploadFile.Length > _options.MaxFileSizeBytes)
        {
            ErrorMessage = string.Format(localizer["FileUpload.Error.MaxFile"], _options.MaxFileSizeMb);
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(_options.StoragePath))
        {
            ErrorMessage = localizer["FileUpload.Error.NoStoragePath"];
            return RedirectToPage();
        }

        try
        {
            Directory.CreateDirectory(_options.StoragePath);
            var usedStorageBytes = GetUsedStorageBytes(_options.StoragePath);
            if (usedStorageBytes >= _options.MaxStorageSizeBytes)
            {
                ErrorMessage = localizer["FileUpload.Error.StorageFull"];
                return RedirectToPage();
            }

            if (usedStorageBytes + uploadFile.Length > _options.MaxStorageSizeBytes)
            {
                ErrorMessage = localizer["FileUpload.Error.StorageWouldOverflow"];
                return RedirectToPage();
            }

            var safeFileName = BuildSafeFileName(uploadFile.FileName);
            var destinationPath = Path.Combine(_options.StoragePath, safeFileName);
            var triggerType = System.IO.File.Exists(destinationPath)
                ? "REPLACE_UPLOAD"
                : "NEW_UPLOAD";
            ArchiveExistingFile(destinationPath);

            await using var destinationStream = System.IO.File.Create(destinationPath);
            await uploadFile.CopyToAsync(destinationStream, HttpContext.RequestAborted);

            var actorUser = userNameResolver.ResolveUserName(User);
            await TryLogFileActionAsync(
                "UPLOAD",
                safeFileName,
                destinationPath,
                uploadFile.Length,
                actorUser);

            await notificationService.NotifyAsync(
                new FileUploadNotificationEvent(
                    safeFileName,
                    destinationPath,
                    uploadFile.Length,
                    actorUser,
                    triggerType,
                    DateTimeOffset.Now),
                HttpContext.RequestAborted);

            SetSuccessMessage(string.Format(
                localizer["FileUpload.Success.Uploaded"],
                Path.GetFileName(destinationPath),
                FormatFileSize(uploadFile.Length)));
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(localizer["FileUpload.Error.Unauthorized"], _options.StoragePath);
        }
        catch (IOException exception)
        {
            ErrorMessage = string.Format(localizer["FileUpload.Error.WriteFailed"], _options.StoragePath, exception.Message);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = localizer["FileUpload.Error.Cancelled"];
        }

        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostSaveNotificationAsync()
    {
        if (!await CanOpenAsync())
        {
            ErrorMessage = localizer["FileUpload.Error.AccessDenied"];
            return RedirectToPage();
        }

        try
        {
            var userName = userNameResolver.ResolveUserName(User);
            var context = BuildNotificationVerificationContext(
                NotificationPattern,
                NotificationTriggerType,
                NotificationChannel,
                NotificationContact);
            var codeEntered = !string.IsNullOrWhiteSpace(NotificationVerificationCode);
            if (ResendNotificationVerification)
            {
                await contactVerificationService.SendCodeAsync(
                    new ContactVerificationRequest(
                        NotificationVerificationPurpose,
                        NotificationChannel,
                        NotificationContact,
                        context,
                        userName),
                    HttpContext.RequestAborted);
                SetSuccessMessage(ResendNotificationVerification
                    ? "Patvirtinimo kodas buvo issiustas. Jis galioja 10 min.; iveskite naujausia gauta koda ir issaugokite pranesima."
                    : "Patvirtinimo kodas buvo issiustas i nurodyta kontakta. Jis galioja 10 min.; iveskite koda ir issaugokite pranesima.");
                return NotificationPage();
            }

            if (!codeEntered)
            {
                SetErrorMessage("Uzpildzius forma pirma atsisiuskite patvirtinimo koda. Kodas galios 10 min.; tik ji ivedus galima issaugoti pranesima.");
                return NotificationPage();
            }

            if (!contactVerificationService.IsConfirmed(
                NotificationVerificationPurpose,
                NotificationChannel,
                NotificationContact,
                context,
                NotificationVerificationCode))
            {
                SetErrorMessage("Neteisingas arba pasibaiges patvirtinimo kodas. Pasitikrinkite, koki koda gavote; jei laisko nerandate, pasitikrinkite Junk mail vieta.");
                return NotificationPage();
            }

            await notificationService.SaveSubscriptionAsync(
                userName,
                NotificationPattern,
                NotificationTriggerType,
                NotificationChannel,
                NotificationContact,
                HttpContext.RequestAborted);
            contactVerificationService.Clear(NotificationVerificationPurpose);
            SetSuccessMessage("Prenumerata sekmingai priskirta.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or SqlException)
        {
            SetErrorMessage(exception.Message);
        }

        return RedirectToPage();
    }
    private void SetSuccessMessage(string message)
    {
        SuccessMessage = message;
        ErrorMessage = null;
    }

    private void SetErrorMessage(string message)
    {
        ErrorMessage = message;
        SuccessMessage = null;
    }
    private PageResult NotificationPage()
    {
        CanOpen = true;
        LoadStorageState();
        return Page();
    }
    public async Task<IActionResult> OnPostCancelNotificationAsync(int notificationId)
    {
        if (!await CanOpenAsync())
        {
            ErrorMessage = localizer["FileUpload.Error.AccessDenied"];
            return RedirectToPage();
        }

        await notificationService.CancelSubscriptionAsync(
            notificationId,
            userNameResolver.ResolveUserName(User),
            HttpContext.RequestAborted);
        SetSuccessMessage("EXCHANGE_UPLOAD pranesimo poreikis atsauktas.");
        return RedirectToPage();
    }
    public async Task<IActionResult> OnGetDownloadAsync(string fileName)
    {
        if (!await CanOpenAsync())
        {
            return Forbid();
        }

        var safeFileName = BuildSafeFileName(fileName);
        if (!safeFileName.Equals(fileName, StringComparison.Ordinal))
        {
            return BadRequest();
        }

        var fullPath = Path.Combine(_options.StoragePath, safeFileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var fileInfo = new FileInfo(fullPath);
        await TryLogFileActionAsync(
            "DOWNLOAD",
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length,
            userNameResolver.ResolveUserName(User));

        return PhysicalFile(
            fileInfo.FullName,
            "application/octet-stream",
            fileInfo.Name);
    }

    private async Task<bool> CanOpenAsync()
    {
        try
        {
            return await accessService.HasAccessAsync(
                User,
                HttpContext.RequestAborted);
        }
        catch
        {
            return false;
        }
    }

    public string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.##} MB";
        }

        return bytes >= 1024L
            ? $"{bytes / 1024d:0.##} KB"
            : $"{bytes} B";
    }

    private void LoadStorageState()
    {
        StorageExists = Directory.Exists(_options.StoragePath);
        if (!StorageExists)
        {
            return;
        }

        try
        {
            UsedStorageBytes = GetUsedStorageBytes(_options.StoragePath);
            NotificationSubscriptions = notificationService
                .ListForUserAsync(userNameResolver.ResolveUserName(User), HttpContext.RequestAborted)
                .GetAwaiter()
                .GetResult();
            RecentFiles = LoadRecentFilesFromLog();
            if (RecentFiles.Count == 0)
            {
                RecentFiles = Directory
                    .EnumerateFiles(_options.StoragePath)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(10)
                    .Select(file => new UploadedFileInfo(
                        file.Name,
                        file.Length,
                        file.LastWriteTime,
                        null,
                        null))
                    .ToArray();
            }
        }
        catch
        {
            UsedStorageBytes = 0;
            RecentFiles = [];
        }
    }

    private static long GetUsedStorageBytes(string directoryPath)
    {
        return Directory
            .EnumerateFiles(directoryPath)
            .Sum(path => new FileInfo(path).Length);
    }
    public string NotificationTriggerLabel(string triggerType) => triggerType switch
    {
        "NEW_UPLOAD" => "Naujas failas",
        "REPLACE_UPLOAD" => "Atnaujintas failas",
        "ANY_UPLOAD" => "Bet koks ikelimas",
        _ => triggerType
    };

    public string NotificationContactFor(FileUploadNotificationSubscription subscription)
    {
        return subscription.NotifyChannel.Equals("SMS", StringComparison.OrdinalIgnoreCase)
            ? subscription.PhoneNo
            : subscription.Email;
    }

    private static string BuildNotificationVerificationContext(
        string pattern,
        string triggerType,
        string channel,
        string contact)
    {
        return $"{pattern.Trim()}|{triggerType.Trim().ToUpperInvariant()}|{channel.Trim().ToUpperInvariant()}|{contact.Trim()}";
    }
    private static string BuildSafeFileName(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return $"upload_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            safeFileName = safeFileName.Replace(invalidCharacter, '_');
        }

        return safeFileName.Trim();
    }

    private IReadOnlyList<UploadedFileInfo> LoadRecentFilesFromLog()
    {
        try
        {
            var connectionString = configuration.GetConnectionString(_options.AccessConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return [];
            }

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"""
                WITH latest AS (
                    SELECT
                        FILE_NAME,
                        FILE_PATH,
                        FILE_SIZE_BYTES,
                        ACTOR_USER,
                        EVENT_AT,
                        ROW_NUMBER() OVER (PARTITION BY FILE_NAME ORDER BY EVENT_AT DESC, ID DESC) AS rn
                    FROM {SqlName.NormalizeTableName(_options.LogTableName, "BLS_LOGIT_STAT.dbo.FILE_UPLOAD_LOG")}
                    WHERE ACTION_TYPE = N'UPLOAD'
                )
                SELECT TOP (10)
                    FILE_NAME,
                    FILE_PATH,
                    FILE_SIZE_BYTES,
                    ACTOR_USER,
                    EVENT_AT
                FROM latest
                WHERE rn = 1
                ORDER BY EVENT_AT DESC;
                """;

            var files = new List<UploadedFileInfo>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var fileName = reader.GetString(0);
                var filePath = reader.GetString(1);
                if (!IsPathInsideStorage(filePath))
                {
                    continue;
                }

                var fileInfo = System.IO.File.Exists(filePath) ? new FileInfo(filePath) : null;
                if (fileInfo is null)
                {
                    continue;
                }

                files.Add(new UploadedFileInfo(
                    fileName,
                    fileInfo?.Length ?? reader.GetInt64(2),
                    fileInfo?.LastWriteTime ?? reader.GetDateTimeOffset(4).LocalDateTime,
                    reader.GetString(3),
                    reader.GetDateTimeOffset(4)));
            }

            return files;
        }
        catch
        {
            return [];
        }
    }
    private bool IsPathInsideStorage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(_options.StoragePath) || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var storageRoot = Path.GetFullPath(_options.StoragePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(filePath);
        return candidate.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase);
    }
    private async Task TryLogFileActionAsync(
        string actionType,
        string fileName,
        string filePath,
        long fileSizeBytes,
        string actorUser)
    {
        try
        {
            var connectionString = configuration.GetConnectionString(_options.AccessConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(HttpContext.RequestAborted);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {SqlName.NormalizeTableName(_options.LogTableName, "BLS_LOGIT_STAT.dbo.FILE_UPLOAD_LOG")}
                (
                    ACTION_TYPE,
                    FILE_NAME,
                    FILE_PATH,
                    FILE_SIZE_BYTES,
                    ACTOR_USER,
                    EVENT_AT
                )
                VALUES
                (
                    @ActionType,
                    @FileName,
                    @FilePath,
                    @FileSizeBytes,
                    @ActorUser,
                    SYSDATETIMEOFFSET()
                );
                """;
            command.Parameters.Add("@ActionType", SqlDbType.NVarChar, 20).Value = actionType;
            command.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = fileName;
            command.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1000).Value = filePath;
            command.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt).Value = fileSizeBytes;
            command.Parameters.Add("@ActorUser", SqlDbType.NVarChar, 256).Value = actorUser;

            await command.ExecuteNonQueryAsync(HttpContext.RequestAborted);
        }
        catch
        {
            // Upload/download should not fail just because audit logging is temporarily unavailable.
        }
    }

    private static void ArchiveExistingFile(string destinationPath)
    {
        if (!System.IO.File.Exists(destinationPath))
        {
            return;
        }

        var fileInfo = new FileInfo(destinationPath);
        var directoryPath = fileInfo.DirectoryName
            ?? throw new InvalidOperationException("Nepavyko nustatyti failo katalogo.");
        var archivedFileName = $"OLD_{fileInfo.LastWriteTime:yyyyMMdd_HHmmss}_{fileInfo.Name}";
        var archivePath = Path.Combine(directoryPath, archivedFileName);
        var duplicateIndex = 1;
        while (System.IO.File.Exists(archivePath))
        {
            archivePath = Path.Combine(
                directoryPath,
                $"OLD_{fileInfo.LastWriteTime:yyyyMMdd_HHmmss}_{duplicateIndex}_{fileInfo.Name}");
            duplicateIndex++;
        }

        System.IO.File.Move(destinationPath, archivePath);
    }
}













