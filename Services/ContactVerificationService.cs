using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class ContactVerificationService(
    IHttpContextAccessor httpContextAccessor,
    ISendMailService sendMailService,
    ISendSmsService sendSmsService) : IContactVerificationService
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(10);

    public async Task SendCodeAsync(
        ContactVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var channel = NormalizeChannel(request.Channel);
        var contact = NormalizeContact(channel, request.Contact);
        var purpose = NormalizeRequired(request.Purpose, 80, "Nepavyko nustatyti patvirtinimo paskirties.");
        var context = NormalizeRequired(request.Context, 500, "Nepavyko nustatyti patvirtinimo konteksto.");
        var createdBy = NormalizeRequired(request.CreatedBy, 256, "Nepavyko nustatyti vartotojo.");
        var code = GenerateCode();

        Store(new ContactVerificationState(
            purpose,
            channel,
            contact,
            context,
            code,
            DateTimeOffset.Now.Add(VerificationLifetime)));

        if (channel == "SMS")
        {
            await sendSmsService.SendAsync(
                new SendSmsRequest(
                    contact,
                    $"{purpose} patvirtinimo kodas: {code}. Galioja 10 min.",
                    createdBy,
                    $"{purpose}_VERIFY"),
                cancellationToken);
            return;
        }

        var draft = sendMailService.BuildDraft(
            "Data Exchange Hub",
            $"{purpose} patvirtinimas",
            contact,
            contact,
            $"Patvirtinimo kodas: {code}\n\nKodas galioja 10 min.\nKontekstas: {context}",
            createdBy);
        await sendMailService.SendAsync(draft, cancellationToken);
    }

    public bool IsConfirmed(
        string purpose,
        string channel,
        string contact,
        string context,
        string enteredCode)
    {
        var pending = Read(purpose);
        if (pending is null)
        {
            return false;
        }

        if (pending.ExpiresAt < DateTimeOffset.Now)
        {
            Clear(purpose);
            return false;
        }

        return string.Equals(pending.Channel, NormalizeChannel(channel), StringComparison.OrdinalIgnoreCase)
            && string.Equals(pending.Contact, NormalizeContact(pending.Channel, contact), StringComparison.OrdinalIgnoreCase)
            && string.Equals(pending.Context, NormalizeRequired(context, 500, "Kontekstas tuscias."), StringComparison.Ordinal)
            && string.Equals(pending.Code, NormalizeCode(enteredCode), StringComparison.OrdinalIgnoreCase);
    }

    public bool HasPending(
        string purpose,
        string channel,
        string contact,
        string context)
    {
        var pending = Read(purpose);
        return pending is not null
            && pending.ExpiresAt >= DateTimeOffset.Now
            && string.Equals(pending.Channel, NormalizeChannel(channel), StringComparison.OrdinalIgnoreCase)
            && string.Equals(pending.Contact, NormalizeContact(pending.Channel, contact), StringComparison.OrdinalIgnoreCase)
            && string.Equals(pending.Context, NormalizeRequired(context, 500, "Kontekstas tuscias."), StringComparison.Ordinal);
    }

    public void Clear(string purpose)
    {
        HttpContext.Session.Remove(SessionKey(purpose));
    }

    private void Store(ContactVerificationState pending)
    {
        HttpContext.Session.SetString(
            SessionKey(pending.Purpose),
            JsonSerializer.Serialize(pending));
    }

    private ContactVerificationState? Read(string purpose)
    {
        var value = HttpContext.Session.GetString(SessionKey(purpose));
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ContactVerificationState>(value);
        }
        catch
        {
            Clear(purpose);
            return null;
        }
    }

    private HttpContext HttpContext => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("HTTP kontekstas nepasiekiamas.");

    private static string SessionKey(string purpose) => $"CONTACT_VERIFICATION_{NormalizeRequired(purpose, 80, "Paskirtis tuscia.").ToUpperInvariant()}";

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[6];
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(code);
    }

    private static string NormalizeChannel(string value)
    {
        var channel = NormalizeRequired(value, 20, "Pasirinkite pranesimo buda: EMAIL arba SMS.").ToUpperInvariant();
        return channel is "EMAIL" or "SMS" ? channel : throw new InvalidOperationException("Pasirinkite pranesimo buda: EMAIL arba SMS.");
    }

    private static string NormalizeContact(string channel, string value)
    {
        var contact = NormalizeRequired(value, channel == "EMAIL" ? 320 : 50, channel == "EMAIL" ? "Iveskite el. pasta." : "Iveskite telefono numeri.");
        if (channel == "EMAIL" && (!contact.Contains('@') || contact.Contains(' ')))
        {
            throw new InvalidOperationException("Iveskite teisinga el. pasta.");
        }

        if (channel == "SMS" && !Regex.IsMatch(contact, @"^\+370-?\d{8}$"))
        {
            throw new InvalidOperationException("SMS pranesimams iveskite lietuviska telefono numeri, pvz. +37061234567.");
        }

        return contact;
    }

    private static string NormalizeCode(string value) => NormalizeRequired(value, 20, "Iveskite patvirtinimo koda.").Replace(" ", string.Empty).ToUpperInvariant();

    private static string NormalizeRequired(string value, int maxLength, string errorMessage)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

