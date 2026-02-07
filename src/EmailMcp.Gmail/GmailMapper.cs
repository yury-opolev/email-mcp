using System.Globalization;
using System.Net.Mail;
using EmailMcp.Abstractions;
using Google.Apis.Gmail.v1.Data;

namespace EmailMcp.Gmail;

/// <summary>
/// Maps Gmail API message types to provider-agnostic EmailMessage models.
/// </summary>
public static class GmailMapper
{
    public static EmailMessage ToEmailMessage(Message gmailMessage, bool includeBody = false)
    {
        var headers = gmailMessage.Payload?.Headers ?? [];

        return new EmailMessage
        {
            Id = gmailMessage.Id,
            ThreadId = gmailMessage.ThreadId,
            Subject = GetHeader(headers, "Subject"),
            From = ParseEmailAddress(GetHeader(headers, "From")),
            To = ParseEmailAddresses(GetHeader(headers, "To")),
            Cc = ParseEmailAddresses(GetHeader(headers, "Cc")),
            Bcc = ParseEmailAddresses(GetHeader(headers, "Bcc")),
            Date = ParseDate(GetHeader(headers, "Date")),
            Snippet = gmailMessage.Snippet,
            Body = includeBody ? ExtractBody(gmailMessage.Payload, "text/plain") : null,
            BodyHtml = includeBody ? ExtractBody(gmailMessage.Payload, "text/html") : null,
            IsUnread = gmailMessage.LabelIds?.Contains("UNREAD") ?? false,
            LabelIds = gmailMessage.LabelIds?.ToList() ?? [],
            Attachments = ExtractAttachments(gmailMessage.Payload),
        };
    }

    private static string? GetHeader(IList<MessagePartHeader> headers, string name) =>
        headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    internal static EmailAddress? ParseEmailAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var mailAddress = new MailAddress(raw);
            return new EmailAddress(mailAddress.Address, mailAddress.DisplayName);
        }
        catch (FormatException)
        {
            return new EmailAddress(raw.Trim());
        }
    }

    internal static IReadOnlyList<EmailAddress> ParseEmailAddresses(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseEmailAddress)
            .Where(a => a is not null)
            .Cast<EmailAddress>()
            .ToList();
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        return null;
    }

    private static string? ExtractBody(MessagePart? payload, string mimeType)
    {
        if (payload is null)
            return null;

        if (string.Equals(payload.MimeType, mimeType, StringComparison.OrdinalIgnoreCase)
            && payload.Body?.Data is not null)
        {
            return DecodeBase64Url(payload.Body.Data);
        }

        if (payload.Parts is null)
            return null;

        foreach (var part in payload.Parts)
        {
            var body = ExtractBody(part, mimeType);
            if (body is not null)
                return body;
        }

        return null;
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var bytes = Convert.FromBase64String(base64);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static IReadOnlyList<EmailAttachment> ExtractAttachments(MessagePart? payload)
    {
        if (payload?.Parts is null)
            return [];

        return payload.Parts
            .Where(p => !string.IsNullOrEmpty(p.Filename))
            .Select(p => new EmailAttachment
            {
                Filename = p.Filename,
                MimeType = p.MimeType ?? "application/octet-stream",
                Size = p.Body?.Size ?? 0,
                AttachmentId = p.Body?.AttachmentId,
            })
            .ToList();
    }
}
