using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class ListEmailsTool
{
    [McpServerTool(Name = "list_emails"), Description(
        "Lists recent emails from the inbox. Optionally filter by label ID (e.g., 'INBOX', 'SENT', 'DRAFT'). " +
        "Returns email ID, subject, sender, date, and snippet for each message.")]
    public static async Task<string> ListEmails(
        IEmailProvider emailProvider,
        [Description("Maximum number of emails to return (1-50, default 20)")] int maxResults = 20,
        [Description("Optional label ID to filter by (e.g., 'INBOX', 'SENT', 'STARRED', 'UNREAD')")] string? labelId = null,
        CancellationToken cancellationToken = default)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);
        var emails = await emailProvider.ListEmailsAsync(maxResults, labelId, cancellationToken);

        var result = emails.Select(e => new
        {
            e.Id,
            e.Subject,
            From = e.From?.ToString(),
            Date = e.Date?.ToString("yyyy-MM-dd HH:mm"),
            e.Snippet,
            e.IsUnread,
            Labels = e.LabelIds,
        });

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
