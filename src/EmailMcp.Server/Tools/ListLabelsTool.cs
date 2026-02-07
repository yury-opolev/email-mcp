using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class ListLabelsTool
{
    [McpServerTool(Name = "list_labels"), Description(
        "Lists all email labels/folders available in the account. " +
        "Returns label IDs and names. Use label IDs with list_emails or search_emails to filter by label.")]
    public static async Task<string> ListLabels(
        IEmailProvider emailProvider,
        CancellationToken cancellationToken = default)
    {
        var labels = await emailProvider.ListLabelsAsync(cancellationToken);

        var result = labels.Select(l => new
        {
            l.Id,
            l.Name,
            l.Type,
            l.UnreadCount,
            l.TotalCount,
        });

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
