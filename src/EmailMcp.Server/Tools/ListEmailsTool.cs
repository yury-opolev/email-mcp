using System.ComponentModel;
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
        IAccountRegistry accounts,
        [Description("Maximum number of emails to return (1-50, default 20)")] int maxResults = 20,
        [Description("Optional label ID to filter by (e.g., 'INBOX', 'SENT', 'STARRED', 'UNREAD')")] string? labelId = null,
        [Description(AccountParameter.Description)] string? account = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var emailProvider = await accounts.GetProviderAsync(account, cancellationToken);
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

            return ToolResponse.Json(result);
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
