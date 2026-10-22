using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace DATA_EXCHANGE_HUB.Models;

public sealed record DikeClientInfo(
    long OwnerId,
    string FirmId,
    string Sid,
    string ShortName,
    string Name,
    string RegCode,
    string Address,
    string ManagerFullName = "");

public sealed class DikeAgreementInput : IValidatableObject
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public long OwnerId { get; set; }
    [Required(ErrorMessage = "Dike.Validation.Required"), StringLength(200, ErrorMessage = "Dike.Validation.Length")] public string ContractNo { get; set; } = "";
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(200, ErrorMessage = "Dike.Validation.Length")] public string Appendix { get; set; } = "";
    [Required(ErrorMessage = "Dike.Validation.Required")] public string ValidFrom { get; set; } = "";
    public string Frequency { get; set; } = "Kas menesi";
    public string Flow { get; set; } = "IN";
    [Range(0, 100, ErrorMessage = "Dike.Validation.Percent")] public decimal WriteOffPercent { get; set; }
    [Required(ErrorMessage = "Dike.Validation.Required")] public string FirstPeriod { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string Manager { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<string> InfPrekCodes { get; set; } = [];
    public bool AutomaticSending { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(1000, ErrorMessage = "Dike.Validation.Length")] public string Recipients { get; set; } = "";
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(200, ErrorMessage = "Dike.Validation.Length")] public string MonthDays { get; set; } = "";
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string SendTime { get; set; } = "09:00";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OwnerId <= 0) yield return new("Dike.Validation.Client", [nameof(OwnerId)]);
        if (!DateOnly.TryParseExact(ValidFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            yield return new("Dike.Validation.Date", [nameof(ValidFrom)]);
        if (!Regex.IsMatch(FirstPeriod ?? "", "^[0-9]{4}-?(0[1-9]|1[0-2])$") ||
            !DateOnly.TryParseExact(FirstPeriod!.Replace("-", "") + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            yield return new("Dike.Validation.Period", [nameof(FirstPeriod)]);
        if (Frequency is not ("Kas menesi" or "Kas ketvirti" or "Pagal poreiki"))
            yield return new("Dike.Validation.Frequency", [nameof(Frequency)]);
        if (Flow is not ("IN" or "OUT")) yield return new("Dike.Validation.Flow", [nameof(Flow)]);
        if (InfPrekCodes.Count > 500 || InfPrekCodes.Any(code => !Regex.IsMatch(code ?? "", "^[A-Za-z0-9]{7}$")))
            yield return new("Dike.Validation.Codes", [nameof(InfPrekCodes)]);
        var recipients = Split(Recipients ?? "");
        if ((AutomaticSending || !string.IsNullOrWhiteSpace(Recipients)) &&
            (recipients.Length == 0 || recipients.Any(email => !MailAddress.TryCreate(email, out var address) || address.Address != email)))
            yield return new("Dike.Validation.Email", [nameof(Recipients)]);
        if (AutomaticSending)
        {
            var days = Split(MonthDays);
            if (days.Length == 0 || days.Any(day => !int.TryParse(day, out var number) || number < 1 || number > 31))
                yield return new("Dike.Validation.Days", [nameof(MonthDays)]);
        }
        if ((AutomaticSending || !string.IsNullOrWhiteSpace(SendTime)) &&
            !TimeOnly.TryParseExact(SendTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            yield return new("Dike.Validation.Time", [nameof(SendTime)]);
    }

    public static string[] Split(string value) => value.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed record DikeAgreement(DikeAgreementInput Data, string ClientName, DateTime UpdatedAt, string UpdatedBy);
public sealed record DikeChange(string Field, string Before, string After);
public sealed record DikeAudit(long Id, Guid AgreementId, long OwnerId, DateTime OccurredAt, string Actor,
    string Action, string ClientName, IReadOnlyList<DikeChange> Changes, string? DocumentFileName = null);

public sealed record DikeDocument(Guid Id, Guid AgreementId, string FileName, string Note, string ContentType,
    long Size, DateTime UploadedAt, string UploadedBy);
public sealed record DikeDocumentContent(DikeDocument Document, byte[] Bytes);
