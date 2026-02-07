using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class SearchEmailsTool
{
    [McpServerTool(Name = "search_emails"), Description(
        "Searches emails using a query string. Supports Gmail search syntax " +
        "(e.g., 'from:john subject:meeting after:2025/01/01'). " +
        "Can also filter by individual fields: from, to, subject, date range, and label.")]
    public static async Task<string> SearchEmails(
        IEmailProvider emailProvider,
        [Description("Search query string (Gmail syntax, e.g., 'from:alice subject:report is:unread')")] string? query = null,
        [Description("Filter by sender email address")] string? from = null,
        [Description("Filter by recipient email address")] string? to = null,
        [Description("Filter by subject text")] string? subject = null,
        [Description("Only emails after this date (format: yyyy-MM-dd)")] string? after = null,
        [Description("Only emails before this date (format: yyyy-MM-dd)")] string? before = null,
        [Description("Filter by label ID")] string? labelId = null,
        [Description("Maximum number of results (1-50, default 20)")] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);

        var searchQuery = new EmailSearchQuery
        {
            Query = query,
            From = from,
            To = to,
            Subject = subject,
            After = ParseDate(after),
            Before = ParseDate(before),
            LabelId = labelId,
            MaxResults = maxResults,
        };

        var emails = await emailProvider.SearchEmailsAsync(searchQuery, cancellationToken);

        var result = emails.Select(e => new
        {
            e.Id,
            e.Subject,
            From = e.From?.ToString(),
            Date = e.Date?.ToString("yyyy-MM-dd HH:mm"),
            e.Snippet,
            e.IsUnread,
        });

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    private static DateTimeOffset? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        if (DateTimeOffset.TryParse(dateStr, out var date))
            return date;

        return null;
    }
}
