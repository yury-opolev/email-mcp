using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class ReadEmailTool
{
    [McpServerTool(Name = "read_email"), Description(
        "Reads a specific email by its message ID. Returns the full email content including body, " +
        "headers, attachments info, and labels. Use list_emails or search_emails first to find message IDs.")]
    public static async Task<string> ReadEmail(
        IEmailProvider emailProvider,
        [Description("The email message ID to read")] string messageId,
        CancellationToken cancellationToken = default)
    {
        var email = await emailProvider.GetEmailAsync(messageId, cancellationToken);

        var result = new
        {
            email.Id,
            email.ThreadId,
            email.Subject,
            From = email.From?.ToString(),
            To = email.To.Select(a => a.ToString()).ToList(),
            Cc = email.Cc.Select(a => a.ToString()).ToList(),
            Date = email.Date?.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            email.Body,
            email.IsUnread,
            Labels = email.LabelIds,
            Attachments = email.Attachments.Select(a => new
            {
                a.Filename,
                a.MimeType,
                a.Size,
            }).ToList(),
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
