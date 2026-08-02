using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class ListAccountsTool
{
    [McpServerTool(Name = "list_accounts"), Description(
        "Lists the configured email accounts: alias, the real address behind it once known, " +
        "which is the default, and whether it is signed in. Never returns secrets. " +
        "The address is filled in after the first successful authentication, so an alias bound " +
        "to the wrong Google account is visible here.")]
    public static async Task<string> ListAccounts(
        IAccountRegistry accounts,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configured = await accounts.ListAccountsAsync(cancellationToken);

            if (configured.Count == 0)
            {
                return ToolResponse.Json(new
                {
                    Accounts = Array.Empty<object>(),
                    Message = "No accounts are configured. Use 'setup_gmail' to get started.",
                });
            }

            string? defaultAlias = null;
            try
            {
                defaultAlias = await accounts.ResolveAliasAsync(null, cancellationToken);
            }
            catch (AccountException)
            {
                // Several accounts with no default chosen. Leave it unset rather than guessing.
            }

            var rows = new List<object>(configured.Count);
            foreach (var item in configured)
            {
                rows.Add(new
                {
                    item.Alias,
                    Email = item.EmailAddress,
                    IsDefault = item.Alias == defaultAlias,
                    IsAuthenticated = await accounts.IsAuthenticatedAsync(item.Alias, cancellationToken),
                    Created = item.CreatedUtc.ToString("yyyy-MM-dd"),
                });
            }

            return ToolResponse.Json(new
            {
                Accounts = rows,
                Default = defaultAlias,
                Note = defaultAlias is null
                    ? "Several accounts are configured and none is marked default. "
                      + "Pass 'account' explicitly, or run 'set_default_account'."
                    : null,
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
