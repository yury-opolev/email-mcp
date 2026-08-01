using System.ComponentModel;
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
        IAccountRegistry accounts,
        [Description(AccountParameter.Description)] string? account = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var emailProvider = await accounts.GetProviderAsync(account, cancellationToken);
            var labels = await emailProvider.ListLabelsAsync(cancellationToken);

            var result = labels.Select(l => new
            {
                l.Id,
                l.Name,
                l.Type,
                l.UnreadCount,
                l.TotalCount,
            });

            return ToolResponse.Json(result);
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
