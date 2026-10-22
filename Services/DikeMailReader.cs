using System.Text;

namespace DATA_EXCHANGE_HUB.Services;

public sealed record DikeMailContent(string From, string To, string Subject, string Date, string Text,
    string Html, IReadOnlyList<string> Attachments);

public static class DikeMailReader
{
    public static DikeMailContent Read(byte[] bytes)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream = new MemoryStream(bytes, writable: false);
            var message = MsgReader.Mime.Message.Load(stream, false, false);
            if (message.Headers.From is null && string.IsNullOrWhiteSpace(message.Headers.Subject) && (message.Headers.To?.Count ?? 0) == 0)
                throw new FormatException();
            return new(message.Headers.From?.ToString() ?? "", string.Join(", ", message.Headers.To ?? []),
                message.Headers.Subject ?? "", message.Headers.Date ?? "", message.TextBody?.GetBodyAsText() ?? "",
                message.HtmlBody?.GetBodyAsText() ?? "", message.Attachments?.Select(part => part.FileName ?? "").ToArray() ?? []);
        }
        catch { throw new InvalidOperationException("Dike.Document.InvalidContent"); }
    }
}
