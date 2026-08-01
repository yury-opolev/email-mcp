using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class RemoveAccountTool
{
    [McpServerTool(Name = "remove_account"), Description(
        "Removes an account and deletes its stored credentials and sign-in. " +
        "By default the OAuth grant is also revoked with Google, so the app loses access to that " +
        "mailbox entirely; pass revokeRemote=false to delete only the local copies and leave the " +
        "grant in place. If the removed account was the default and exactly one account remains, " +
        "that one becomes the default.")]
    public static async Task<string> RemoveAccount(
        IAccountRegistry accounts,
        [Description("Alias of the account to remove.")] string alias,
        [Description("Also revoke the OAuth grant with Google. Defaults to true.")] bool revokeRemote = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.RemoveAccountAsync(alias, revokeRemote, cancellationToken);
            var normalized = AccountAlias.Normalize(alias);
            var remaining = await accounts.ListAccountsAsync(cancellationToken);

            return ToolResponse.Json(new
            {
                Success = true,
                Removed = normalized,
                RevokedWithGoogle = revokeRemote,
                Remaining = remaining.Select(a => a.Alias),
                Message = revokeRemote
                    ? $"Account '{normalized}' removed and its Google grant revoked."
                    : $"Account '{normalized}' removed locally. The Google grant is still in place; "
                      + "revoke it at https://myaccount.google.com/permissions if you want it gone.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
