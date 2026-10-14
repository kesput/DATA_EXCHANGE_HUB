using DATA_EXCHANGE_HUB.Models;
using DATA_EXCHANGE_HUB.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MsgReader.Outlook;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Pages.Seshat;

[RequestSizeLimit(110L * 1024L * 1024L)]
[RequestFormLimits(MultipartBodyLengthLimit = 110L * 1024L * 1024L)]
public sealed class IndexModel(ISeshatService seshatService, ISendSmsService sendSmsService, IContactVerificationService contactVerificationService, IOptionsMonitor<SeshatReminderOptions> reminderOptions) : PageModel
{
    private const string SmsVerificationSessionKey = "SESHAT_SMS_VERIFICATION";
    private static readonly TimeSpan SmsVerificationLifetime = TimeSpan.FromMinutes(10);
    [BindProperty(SupportsGet = true)]
    public int? DocumentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Country { get; set; }

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public string Keywords { get; set; } = string.Empty;

    [BindProperty]
    public string EditStatus { get; set; } = "ACTIVE";

    [BindProperty]
    public DateTime? ValidFrom { get; set; }

    [BindProperty]
    public DateTime? ValidTo { get; set; }

    [BindProperty]
    public int? ReminderDaysBefore { get; set; }

    [BindProperty]
    public string CountriesText { get; set; } = string.Empty;

    [BindProperty]
    public string FileDescription { get; set; } = string.Empty;

    [BindProperty]
    public string SectionTitle { get; set; } = string.Empty;

    [BindProperty]
    public string SectionBody { get; set; } = string.Empty;

    [BindProperty]
    public string SectionFileDescription { get; set; } = string.Empty;

    [BindProperty]
    public string DeleteReason { get; set; } = string.Empty;
    [BindProperty]
    public string ReminderChannel { get; set; } = "EMAIL";

    [BindProperty]
    public string ReminderContact { get; set; } = string.Empty;

    [BindProperty]
    public int ReminderNotifyDaysBefore { get; set; } = 14;

    [BindProperty]
    public string SmsVerificationCode { get; set; } = string.Empty;

    [BindProperty]
    public bool ResendSmsVerification { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public SeshatAccessState Access { get; private set; } = new(string.Empty, false, new HashSet<string>(), new HashSet<string>());

    public IReadOnlyList<SeshatDocumentSummary> Documents { get; private set; } = [];

    public SeshatDocumentDetails? Details { get; private set; }

    public bool CanEditSelected => Details is not null && Access.CanContributeAny(Details.Document.Countries);

    public string ReminderSendTimeText => reminderOptions.CurrentValue.SendTimeText;

    public async Task OnGetAsync()
    {
        await LoadAsync();
        if (Details is not null)
        {
            ApplySelectedToForm();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(List<IFormFile> uploadFiles)
    {
        await LoadAccessOnlyAsync();
        if (!Access.CanCreate)
        {
            ErrorMessage = "Neturite SESHAT CONTRIBUTE teises.";
            return RedirectToPage();
        }

        try
        {
            var wasExistingDocument = DocumentId.HasValue;
            var savedId = await seshatService.SaveDocumentAsync(
                DocumentId,
                Title,
                Description,
                Keywords,
                EditStatus,
                ValidFrom,
                ValidTo,
                CountriesText,
                ReminderDaysBefore,
                Access,
                HttpContext.RequestAborted);

            if (uploadFiles.Any(file => file.Length > 0))
            {
                await seshatService.UploadFilesAsync(
                    savedId,
                    uploadFiles,
                    FileDescription,
                    null,
                    Access,
                    HttpContext.RequestAborted);
                SuccessMessage = wasExistingDocument
                    ? $"SESHAT irasas atnaujintas, failai prikabinti: {uploadFiles.Count(file => file.Length > 0)}."
                    : $"SESHAT irasas sukurtas, failai prikabinti: {uploadFiles.Count(file => file.Length > 0)}.";
            }
            else
            {
                SuccessMessage = wasExistingDocument ? "SESHAT irasas atnaujintas." : "SESHAT irasas sukurtas.";
            }

            return RedirectToPage(new { DocumentId = savedId, Search, Status, Country });
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> uploadFiles)
    {
        if (!DocumentId.HasValue)
        {
            ErrorMessage = "Pirma pasirinkite SESHAT irasa.";
            return RedirectToPage();
        }

        await LoadAccessOnlyAsync();
        if (!uploadFiles.Any(file => file.Length > 0))
        {
            ErrorMessage = "Pasirinkite faila.";
            return RedirectToPage(new { DocumentId });
        }

        try
        {
            await seshatService.UploadFilesAsync(
                DocumentId.Value,
                uploadFiles,
                FileDescription,
                null,
                Access,
                HttpContext.RequestAborted);
            SuccessMessage = $"Failai prikabinti: {uploadFiles.Count(file => file.Length > 0)}.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }

    public async Task<IActionResult> OnPostCreateSectionAsync(List<IFormFile> sectionUploadFiles)
    {
        if (!DocumentId.HasValue)
        {
            ErrorMessage = "Pirma pasirinkite SESHAT irasa.";
            return RedirectToPage();
        }

        await LoadAccessOnlyAsync();
        try
        {
            await seshatService.CreateSectionAsync(
                DocumentId.Value,
                SectionTitle,
                SectionBody,
                sectionUploadFiles,
                SectionFileDescription,
                Access,
                HttpContext.RequestAborted);
            SuccessMessage = "Papildomas susitarimo punktas sukurtas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }

    public async Task<IActionResult> OnPostUpdateSectionAsync(int sectionId, string sectionTitle, string sectionBody, string sectionFileDescription, List<IFormFile> sectionUploadFiles)
    {
        if (!DocumentId.HasValue)
        {
            ErrorMessage = "Pirma pasirinkite SESHAT irasa.";
            return RedirectToPage();
        }

        await LoadAccessOnlyAsync();
        try
        {
            await seshatService.UpdateSectionAsync(
                sectionId,
                sectionTitle,
                sectionBody,
                sectionUploadFiles,
                sectionFileDescription,
                Access,
                HttpContext.RequestAborted);
            SuccessMessage = "Papildomas susitarimo punktas atnaujintas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }

    public async Task<IActionResult> OnPostSaveReminderAsync()
    {
        if (!DocumentId.HasValue)
        {
            ErrorMessage = "Pirma pasirinkite SESHAT irasa.";
            return RedirectToPage();
        }

        await LoadAccessOnlyAsync();
        try
        {
            var channel = NormalizeReminderChannel(ReminderChannel);
            var contact = NormalizeReminderContact(ReminderContact);
            var verificationContext = BuildReminderVerificationContext(DocumentId.Value, ReminderNotifyDaysBefore, channel, contact);
            var codeEntered = !string.IsNullOrWhiteSpace(SmsVerificationCode);
            if (ResendSmsVerification || !codeEntered)
            {
                await contactVerificationService.SendCodeAsync(
                    new ContactVerificationRequest(
                        "SESHAT_REMINDER",
                        channel,
                        contact,
                        verificationContext,
                        Access.UserName),
                    HttpContext.RequestAborted);
                SuccessMessage = ResendSmsVerification
                    ? "Issiustas naujas patvirtinimo kodas. Iveskite naujausia gauta koda."
                    : "I nurodyta kontakta issiustas patvirtinimo kodas. Iveskite ji ir dar karta issaugokite pranesima.";
                await LoadAsync();
                return Page();
            }

            if (!contactVerificationService.IsConfirmed(
                "SESHAT_REMINDER",
                channel,
                contact,
                verificationContext,
                SmsVerificationCode))
            {
                ErrorMessage = "Neteisingas arba pasibaiges patvirtinimo kodas. Patikrinkite koda arba issiuskite nauja.";
                await LoadAsync();
                return Page();
            }

            await seshatService.SaveReminderAsync(
                DocumentId.Value,
                channel,
                contact,
                ReminderNotifyDaysBefore,
                Access,
                HttpContext.RequestAborted);
            contactVerificationService.Clear("SESHAT_REMINDER");

            SuccessMessage = "Pranesimo poreikis patvirtintas ir issaugotas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }

    public async Task<IActionResult> OnPostCancelReminderAsync(int reminderId)
    {
        if (!DocumentId.HasValue)
        {
            ErrorMessage = "Pirma pasirinkite SESHAT irasa.";
            return RedirectToPage();
        }

        await LoadAccessOnlyAsync();
        try
        {
            await seshatService.CancelReminderAsync(reminderId, Access, HttpContext.RequestAborted);
            SuccessMessage = "Pranesimo poreikio atsisakyta.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }
    public async Task<IActionResult> OnPostSendReminderTestAsync(int? reminderId)
    {
        if (!DocumentId.HasValue)
        {
            ErrorMessage = "Pirma pasirinkite SESHAT irasa.";
            return RedirectToPage();
        }

        await LoadAccessOnlyAsync();
        try
        {
            await seshatService.SendReminderTestAsync(DocumentId.Value, reminderId, Access, HttpContext.RequestAborted);
            SuccessMessage = "Testinis SESHAT pranesimas issiustas el. pastu.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }
    public async Task<IActionResult> OnGetDownloadAsync(int fileId)
    {
        await LoadAccessOnlyAsync();
        var file = await seshatService.GetFileAsync(fileId, Access, includeDeleted: false, HttpContext.RequestAborted);
        if (file is null)
        {
            return NotFound();
        }

        var path = seshatService.GetPhysicalFilePath(file);
        return System.IO.File.Exists(path)
            ? PhysicalFile(path, file.ContentType, file.OriginalFileName)
            : NotFound();
    }

    public async Task<IActionResult> OnGetPreviewAsync(int fileId)
    {
        await LoadAccessOnlyAsync();
        var file = await seshatService.GetFileAsync(fileId, Access, includeDeleted: false, HttpContext.RequestAborted);
        if (file is null)
        {
            return NotFound();
        }

        var path = seshatService.GetPhysicalFilePath(file);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        if (IsMailFile(file))
        {
            return Content(BuildMailPreviewHtml(file, GetMailPreview(file)), "text/html; charset=utf-8");
        }

        var contentType = GetPreviewContentType(file);
        if (contentType is null)
        {
            return NotFound();
        }

        var result = PhysicalFile(path, contentType);
        result.EnableRangeProcessing = true;
        return result;
    }

    public async Task<IActionResult> OnPostDeleteFileAsync(int fileId)
    {
        await LoadAccessOnlyAsync();
        try
        {
            await seshatService.DeleteFileAsync(fileId, DeleteReason, Access, HttpContext.RequestAborted);
            SuccessMessage = "Failas perkeltas i Deleted kataloga.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }

    public async Task<IActionResult> OnPostRestoreFileAsync(int fileId)
    {
        await LoadAccessOnlyAsync();
        try
        {
            await seshatService.RestoreFileAsync(fileId, Access, HttpContext.RequestAborted);
            SuccessMessage = "Failas atstatytas.";
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatError(exception);
        }

        return RedirectToPage(new { DocumentId });
    }


    public SeshatMailPreview GetMailPreview(SeshatFileEntry file)
    {
        if (!IsMailFile(file))
        {
            return EmptyMailPreview(file);
        }

        try
        {
            var path = seshatService.GetPhysicalFilePath(file);
            if (!System.IO.File.Exists(path))
            {
                return EmptyMailPreview(file);
            }

            return file.FileKind.Equals("OUTLOOK_EML", StringComparison.OrdinalIgnoreCase)
                ? ReadEmlPreview(file, path)
                : ReadMsgPreview(file, path);
        }
        catch
        {
            return EmptyMailPreview(file);
        }
    }

    public static bool CanPreview(SeshatFileEntry file)
    {
        return IsMailFile(file) || GetPreviewContentType(file) is not null;
    }

    private static bool IsMailFile(SeshatFileEntry file)
    {
        return file.FileKind.Equals("OUTLOOK_EML", StringComparison.OrdinalIgnoreCase)
            || file.FileKind.Equals("OUTLOOK_MSG", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPreviewContentType(SeshatFileEntry file)
    {
        var extension = Path.GetExtension(file.OriginalFileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".txt" => "text/plain; charset=utf-8",
            ".csv" => "text/plain; charset=utf-8",
            _ => null
        };
    }

    private static SeshatMailPreview ReadEmlPreview(SeshatFileEntry file, string path)
    {
        var lines = System.IO.File.ReadLines(path, Encoding.UTF8).Take(5000).ToArray();
        return new SeshatMailPreview(
            file.FileKind,
            DecodeMimeHeader(ReadMailHeader(lines, "From")),
            DecodeMimeHeader(ReadMailHeader(lines, "To")),
            DecodeMimeHeader(ReadMailHeader(lines, "Cc")),
            ReadMailHeader(lines, "Date"),
            DecodeMimeHeader(ReadMailHeader(lines, "Subject")),
            ReadMailBody(lines),
            string.Empty);
    }

    private static SeshatMailPreview ReadMsgPreview(SeshatFileEntry file, string path)
    {
        using var message = new MsgReader.Outlook.Storage.Message(path);
        var date = message.SentOn ?? message.ReceivedOn;
        return new SeshatMailPreview(
            file.FileKind,
            FormatMailContact(message.Sender?.DisplayName, message.Sender?.Email),
            message.GetEmailRecipients(RecipientType.To, false, false) ?? string.Empty,
            message.GetEmailRecipients(RecipientType.Cc, false, false) ?? string.Empty,
            date.HasValue ? UiFormat.DateTimeMinute(date.Value) : string.Empty,
            message.Subject ?? string.Empty,
            NormalizeMsgBodyText(message.BodyText),
            message.BodyHtml ?? string.Empty);
    }

    private static string FormatMailContact(string? displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return email ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(email)
            ? displayName.Trim()
            : $"{displayName.Trim()} <{email.Trim()}>";
    }

    private static string NormalizeMsgBodyText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return body.Replace("\r\n", "\n").Trim();
    }

    private static string ExtractReadableMsgText(byte[] bytes)
    {
        var unicode = Encoding.Unicode.GetString(bytes).Replace('\0', ' ');
        var utf8 = Encoding.UTF8.GetString(bytes).Replace('\0', ' ');
        return string.Join('\n', ExtractReadableLines(unicode).Concat(ExtractReadableLines(utf8)));
    }

    private static IEnumerable<string> ExtractReadableLines(string value)
    {
        return value
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(IsUsefulMsgTextLine)
            .Take(1200);
    }

    private static bool IsUsefulMsgTextLine(string line)
    {
        if (line.Length is < 3 or > 1000)
        {
            return false;
        }

        if (line.Contains("__substg", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Root Entry", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Ole", StringComparison.OrdinalIgnoreCase)
            || line.Contains('\uFFFD'))
        {
            return false;
        }

        var lettersOrDigits = line.Count(char.IsLetterOrDigit);
        if (lettersOrDigits < 3)
        {
            return false;
        }

        var useful = line.Count(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ".,;:!?@#%&()[]{}+-_/\\'\"<>".Contains(ch));
        if (useful < line.Length * 0.85)
        {
            return false;
        }

        var symbolRuns = Regex.Matches(line, @"[^\p{L}\p{Nd}\s]{6,}").Count;
        return symbolRuns == 0;
    }

    private static string ReadMailHeader(IEnumerable<string> lines, string headerName)
    {
        var prefix = headerName + ":";
        var collected = new List<string>();
        foreach (var line in lines)
        {
            if (collected.Count == 0)
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    collected.Add(line[prefix.Length..].Trim());
                }
                continue;
            }

            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                collected.Add(line.Trim());
                continue;
            }

            break;
        }

        return string.Join(" ", collected).Trim();
    }

    private static string ReadMailHeader(string text, string headerName)
    {
        return ReadMailHeader(text.Split('\n'), headerName);
    }

    private static string ReadMailBody(IEnumerable<string> lines)
    {
        var body = string.Join("\n", lines.SkipWhile(line => !string.IsNullOrWhiteSpace(line)).Skip(1).Take(800)).Trim();
        return DecodeTransferBody(body);
    }

    private static string ReadMsgBody(string text)
    {
        var body = ExtractMimeTextBody(text);
        return LooksLikeReadableBody(body) ? body : ExtractMsgFallbackBody(text);
    }

    private static string ExtractMimeTextBody(string text)
    {
        var markerIndex = text.IndexOf("Content-Type: text/plain", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var part = text[markerIndex..];
        var bodyStart = part.IndexOf("\n\n", StringComparison.Ordinal);
        var separatorLength = 2;
        if (bodyStart < 0)
        {
            bodyStart = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            separatorLength = 4;
        }

        if (bodyStart < 0)
        {
            return string.Empty;
        }

        var body = part[(bodyStart + separatorLength)..];
        var boundaryIndex = body.IndexOf("\n--", StringComparison.Ordinal);
        if (boundaryIndex > 0)
        {
            body = body[..boundaryIndex];
        }

        return DecodeTransferBody(body).Trim();
    }

    private static string ExtractMsgFallbackBody(string text)
    {
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsUsefulMsgTextLine)
            .Where(line => !Regex.IsMatch(line, @"^(From|To|Cc|Bcc|Date|Subject|Message-ID|Content-|MIME-|Thread-|X-|Received):", RegexOptions.IgnoreCase))
            .Distinct()
            .Take(120)
            .ToArray();

        var body = string.Join("\n", lines).Trim();
        return LooksLikeReadableBody(body) ? body : string.Empty;
    }
    private static bool LooksLikeReadableBody(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var sample = value.Length > 1200 ? value[..1200] : value;
        var letters = sample.Count(char.IsLetterOrDigit);
        var controls = sample.Count(char.IsControl);
        return letters >= 20 && controls < sample.Length / 10;
    }

    private static string DecodeTransferBody(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return DecodeQuotedPrintable(value.Trim());
    }

    private static string DecodeMimeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("=?", StringComparison.Ordinal))
        {
            return value;
        }

        return Regex.Replace(value, @"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=", match =>
        {
            try
            {
                var encoding = Encoding.GetEncoding(match.Groups[1].Value);
                var mode = match.Groups[2].Value;
                var payload = match.Groups[3].Value;
                var bytes = mode.Equals("B", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(payload)
                    : DecodeQuotedPrintableBytes(payload.Replace('_', ' '));
                return encoding.GetString(bytes);
            }
            catch
            {
                return match.Value;
            }
        }).Trim();
    }

    private static string DecodeQuotedPrintable(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(DecodeQuotedPrintableBytes(value.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty)));
        }
        catch
        {
            return value;
        }
    }

    private static byte[] DecodeQuotedPrintableBytes(string value)
    {
        using var stream = new MemoryStream();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '=' && index + 2 < value.Length && IsHex(value[index + 1]) && IsHex(value[index + 2]))
            {
                stream.WriteByte(Convert.ToByte(value.Substring(index + 1, 2), 16));
                index += 2;
                continue;
            }

            stream.WriteByte((byte)value[index]);
        }

        return stream.ToArray();
    }

    private static bool IsHex(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static SeshatMailPreview EmptyMailPreview(SeshatFileEntry file)
    {
        return new SeshatMailPreview(file.FileKind, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private static string BuildMailPreviewHtml(SeshatFileEntry file, SeshatMailPreview mail)
    {
        return file.FileKind.Equals("OUTLOOK_MSG", StringComparison.OrdinalIgnoreCase)
            ? BuildOutlookMessagePreviewHtml(file, mail)
            : BuildSimpleMailPreviewHtml(file, mail);
    }

    private static string BuildOutlookMessagePreviewHtml(SeshatFileEntry file, SeshatMailPreview mail)
    {
        static string E(string? value) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value);
        var title = string.IsNullOrWhiteSpace(mail.Subject) ? file.OriginalFileName : mail.Subject;
        var body = BuildMailBodyHtml(mail);
        var initials = BuildInitials(mail.From);
        var repliedLine = string.IsNullOrWhiteSpace(mail.Date)
            ? string.Empty
            : $"<div class=\"reply-line\">&#9432; Message date: {E(mail.Date)}</div>";

        return $$"""
<!doctype html>
<html lang="lt">
<head>
<meta charset="utf-8" />
<title>{{E(title)}}</title>
<style>
html, body { margin: 0; padding: 0; background: #ffffff; color: #111827; font-family: "Segoe UI", Arial, sans-serif; font-size: 13px; }
.outlook-card { min-height: 100vh; background: #ffffff; }
.outlook-head { display: grid; grid-template-columns: 48px 1fr; gap: 12px; align-items: center; background: #626262; color: #ffffff; padding: 13px 18px 10px; }
.avatar { width: 48px; height: 48px; border-radius: 999px; background: #1078d4; display: flex; align-items: center; justify-content: center; font-size: 17px; letter-spacing: .2px; }
.sender { font-size: 15px; font-weight: 600; margin-bottom: 3px; }
.to-line { color: #f3f4f6; font-size: 12px; }
.to-line strong { color: #ffffff; font-weight: 600; }
.reply-line { grid-column: 1 / -1; color: #f3f4f6; font-size: 12px; padding-left: 2px; }
.subject { padding: 14px 18px 0; font-weight: 600; color: #111827; }
.body { padding: 14px 18px 30px; line-height: 1.35; color: #111827; }
.body :first-child { margin-top: 0; }
.body p { margin: 0 0 10px; }
.body a { color: #0563c1; }
.body pre { white-space: pre-wrap; font-family: "Segoe UI", Arial, sans-serif; margin: 0; }
.file-note { padding: 8px 18px; color: #6b7280; font-size: 12px; border-top: 1px solid #e5e7eb; }
</style>
</head>
<body>
<div class="outlook-card">
  <div class="outlook-head">
    <div class="avatar">{{E(initials)}}</div>
    <div>
      <div class="sender">{{E(mail.From)}}</div>
      <div class="to-line">To <strong>{{E(mail.To)}}</strong></div>
    </div>
    {{repliedLine}}
  </div>
  <div class="subject">{{E(title)}}</div>
  <div class="body">{{body}}</div>
  <div class="file-note">{{E(file.OriginalFileName)}} · {{E(file.FileKind)}}</div>
</div>
</body>
</html>
""";
    }

    private static string BuildSimpleMailPreviewHtml(SeshatFileEntry file, SeshatMailPreview mail)
    {
        static string E(string? value) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value);
        var title = string.IsNullOrWhiteSpace(mail.Subject) ? file.OriginalFileName : mail.Subject;
        return $$"""
<!doctype html>
<html lang="lt">
<head>
<meta charset="utf-8" />
<title>{{E(title)}}</title>
<style>
body { font-family: Arial, sans-serif; margin: 18px; color: #111827; }
dl { display: grid; grid-template-columns: 90px 1fr; gap: 8px 14px; }
dt { font-weight: 700; color: #166534; }
dd { margin: 0; }
.mail-body { white-space: pre-wrap; border: 1px solid #d1d5db; border-radius: 6px; padding: 12px; background: #f9fafb; }
</style>
</head>
<body>
<h1>{{E(title)}}</h1>
<dl>
<dt>Failas</dt><dd>{{E(file.OriginalFileName)}}</dd>
<dt>Nuo</dt><dd>{{E(mail.From)}}</dd>
<dt>Kam</dt><dd>{{E(mail.To)}}</dd>
<dt>Kopija</dt><dd>{{E(mail.Cc)}}</dd>
<dt>Data</dt><dd>{{E(mail.Date)}}</dd>
</dl>
<div class="mail-body">{{E(mail.Body)}}</div>
</body>
</html>
""";
    }

    private static string BuildMailBodyHtml(SeshatMailPreview mail)
    {
        if (!string.IsNullOrWhiteSpace(mail.BodyHtml))
        {
            return SanitizeMailHtml(mail.BodyHtml);
        }

        if (!string.IsNullOrWhiteSpace(mail.Body))
        {
            return $"<pre>{WebUtility.HtmlEncode(mail.Body)}</pre>";
        }

        return "<span style=\"color:#6b7280\">Laisko teksto nepavyko perskaityti, bet faila galima parsisiusti.</span>";
    }

    private static string SanitizeMailHtml(string html)
    {
        var body = ExtractHtmlBody(html);
        body = Regex.Replace(body, @"<\s*(script|iframe|object|embed|link|meta)[^>]*>.*?<\s*/\s*\1\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, @"<\s*(script|iframe|object|embed|link|meta)[^>]*/?\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, @"\s+on\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", string.Empty, RegexOptions.IgnoreCase);
        body = Regex.Replace(body, @"javascript\s*:", string.Empty, RegexOptions.IgnoreCase);
        return body.Trim();
    }

    private static string ExtractHtmlBody(string html)
    {
        var match = Regex.Match(html, @"<body[^>]*>(?<body>.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups["body"].Value : html;
    }

    private static string BuildInitials(string value)
    {
        var name = Regex.Replace(value, @"<[^>]+>", string.Empty).Trim();
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "?";
        }

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }


    private async Task SendSmsVerificationAsync(
        int documentId,
        string phoneNo,
        int daysBefore,
        string createdBy,
        CancellationToken cancellationToken)
    {
        ValidateSmsReminderInput(phoneNo, daysBefore);
        var code = GenerateSmsVerificationCode();
        var expiresAt = DateTimeOffset.Now.Add(SmsVerificationLifetime);
        StoreSmsVerification(new PendingSmsVerification(documentId, phoneNo, daysBefore, code, expiresAt));

        await sendSmsService.SendAsync(
            new SendSmsRequest(
                phoneNo,
                $"SESHAT SMS patvirtinimo kodas: {code}. Galioja 10 min.",
                createdBy,
                "SESHAT_SMS_VERIFY"),
            cancellationToken);
    }

    private static string GenerateSmsVerificationCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[6];
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(code);
    }

    private bool IsSmsVerificationConfirmed(
        PendingSmsVerification? pending,
        int documentId,
        string phoneNo,
        int daysBefore,
        string enteredCode)
    {
        if (pending is null || pending.ExpiresAt < DateTimeOffset.Now)
        {
            ClearSmsVerification();
            return false;
        }

        return pending.DocumentId == documentId
            && pending.DaysBefore == daysBefore
            && string.Equals(pending.PhoneNo, phoneNo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pending.Code, NormalizeSmsCode(enteredCode), StringComparison.OrdinalIgnoreCase);
    }

    public bool HasPendingSmsVerification(int documentId, string phoneNo, int daysBefore)
    {
        var pending = ReadSmsVerification();
        return pending is not null
            && pending.ExpiresAt >= DateTimeOffset.Now
            && pending.DocumentId == documentId
            && pending.DaysBefore == daysBefore
            && string.Equals(pending.PhoneNo, NormalizeReminderContact(phoneNo), StringComparison.OrdinalIgnoreCase);
    }

    private void StoreSmsVerification(PendingSmsVerification pending)
    {
        HttpContext.Session.SetString(
            SmsVerificationSessionKey,
            string.Join("|", pending.DocumentId, pending.PhoneNo, pending.DaysBefore, pending.Code, pending.ExpiresAt.ToUnixTimeSeconds()));
    }

    private PendingSmsVerification? ReadSmsVerification()
    {
        var value = HttpContext.Session.GetString(SmsVerificationSessionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('|');
        if (parts.Length != 5
            || !int.TryParse(parts[0], out var documentId)
            || !int.TryParse(parts[2], out var daysBefore)
            || !long.TryParse(parts[4], out var expiresUnix))
        {
            ClearSmsVerification();
            return null;
        }

        return new PendingSmsVerification(
            documentId,
            parts[1],
            daysBefore,
            parts[3],
            DateTimeOffset.FromUnixTimeSeconds(expiresUnix));
    }

    private void ClearSmsVerification()
    {
        HttpContext.Session.Remove(SmsVerificationSessionKey);
    }
    private static string BuildReminderVerificationContext(
        int documentId,
        int daysBefore,
        string channel,
        string contact)
    {
        return $"{documentId}|{daysBefore}|{channel.Trim().ToUpperInvariant()}|{contact.Trim()}";
    }
    private static string NormalizeReminderChannel(string value)
    {
        var channel = (value ?? string.Empty).Trim().ToUpperInvariant();
        return channel is "EMAIL" or "SMS" ? channel : throw new InvalidOperationException("Pasirinkite pranesimo buda: EMAIL arba SMS.");
    }

    private static string NormalizeReminderContact(string value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeSmsCode(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static void ValidateSmsReminderInput(string phoneNo, int daysBefore)
    {
        if (daysBefore is < 1 or > 365)
        {
            throw new InvalidOperationException("Pranesimo dienu skaicius turi buti nuo 1 iki 365.");
        }

        if (!Regex.IsMatch(phoneNo, @"^\+?[0-9][0-9\s\-()]{6,24}$") || phoneNo.Count(char.IsDigit) < 7)
        {
            throw new InvalidOperationException("Iveskite korektiska telefono numeri, pvz. +37061234567.");
        }
    }

    private sealed record PendingSmsVerification(
        int DocumentId,
        string PhoneNo,
        int DaysBefore,
        string Code,
        DateTimeOffset ExpiresAt);
    private string BuildReminderSampleText(SeshatDocumentSummary document, SeshatReminderEntry? reminder)
    {
        var channel = reminder?.NotifyChannel ?? "EMAIL arba SMS";
        var contact = reminder is null
            ? "user@bls.lt arba +370..."
            : reminder.NotifyChannel == "SMS" ? reminder.PhoneNo : reminder.Email;
        var daysBefore = reminder?.DaysBefore ?? document.ReminderDaysBefore ?? 14;
        var subject = reminder?.Subject ?? "SESHAT pranesama apie artejanti susitarimo termino pabaiga";
        var validTo = document.ValidTo.HasValue ? FormatDate(document.ValidTo) : "nenustatyta";
        var countries = document.Countries.Count == 0 ? "-" : string.Join(", ", document.Countries);

        return $"""
SESHAT TESTINIS PRANESIMAS

Siuntimo laikas: {ReminderSendTimeText}
Kanalas: {channel}
Gavejas: {contact}
Pranesama pries: {daysBefore} d. iki termino

Subject: {subject}

Sveiki,

primename, kad SESHAT susitarimo terminas arteja prie pabaigos.

Susitarimas: {document.Title}
SESHAT ID: {document.Id}
Salys: {countries}
Galioja iki: {validTo}

Sis failas yra testinis pranesimo pavyzdys. Realaus EMAIL/SMS siuntimo skriptai bus prijungti veliau.
""";
    }

    public string FormatReminderSendAt(DateTime? validTo, int daysBefore)
    {
        if (!validTo.HasValue)
        {
            return "-";
        }

        return $"{UiFormat.Date(validTo.Value.Date.AddDays(-daysBefore))} {ReminderSendTimeText}";
    }
    public static string FormatDate(DateTime? value)
    {
        return value.HasValue ? UiFormat.Date(value.Value) : "-";
    }

    public static string FormatDateInput(DateTime? value)
    {
        return value.HasValue ? UiFormat.Date(value.Value) : string.Empty;
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return UiFormat.DateTimeMinute(value);
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.##} MB";
        }

        return bytes >= 1024L ? $"{bytes / 1024d:0.##} KB" : $"{bytes} B";
    }

    private async Task LoadAsync()
    {
        await LoadAccessOnlyAsync();
        if (!Access.CanOpen)
        {
            Documents = [];
            Details = null;
            return;
        }

        try
        {
            Documents = await seshatService.ListDocumentsAsync(Access, Search, Status, Country, HttpContext.RequestAborted);
            if (DocumentId.HasValue)
            {
                Details = await seshatService.GetDocumentDetailsAsync(DocumentId.Value, Access, HttpContext.RequestAborted);
            }
        }
        catch (Exception exception)
        {
            ErrorMessage ??= FormatError(exception);
            Documents = [];
            Details = null;
        }
    }

    private async Task LoadAccessOnlyAsync()
    {
        try
        {
            Access = await seshatService.GetAccessAsync(User, HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Access = new SeshatAccessState(string.Empty, false, new HashSet<string>(), new HashSet<string>());
            ErrorMessage ??= FormatError(exception);
        }
    }

    private void ApplySelectedToForm()
    {
        var document = Details!.Document;
        Title = document.Title;
        Description = document.Description;
        Keywords = document.Keywords;
        EditStatus = document.Status;
        ValidFrom = document.ValidFrom;
        ValidTo = document.ValidTo;
        CountriesText = string.Join(", ", document.Countries);
        ReminderDaysBefore = document.ReminderDaysBefore;
    }

    private static string FormatError(Exception exception)
    {
        return exception.Message.Contains("SESHAT_", StringComparison.OrdinalIgnoreCase)
            ? "SESHAT SQL objektai dar nesukurti. Paleiskite Database/Seshat/SESHAT.sql serveryje."
            : exception.Message;
    }
}

public sealed record SeshatMailPreview(string FileKind, string From, string To, string Cc, string Date, string Subject, string Body, string BodyHtml);





















