using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class RevokeAuthTool
{
    [McpServerTool(Name = "revoke_auth"), Description(
        "Fully revokes the OAuth token with Google and deletes all locally stored tokens for one " +
        "account. Use this when you want to completely disconnect the app from that Google account. " +
        "After revoking, run 'auth_status' to re-authenticate. " +
        "This does NOT delete the stored client credentials (Client ID / Secret) - " +
        "use 'setup_gmail' to change those, or 'remove_account' to delete the account.")]
    public static async Task<string> RevokeAuth(
        IAccountRegistry accounts,
        [Description(AccountParameter.Description)] string? account = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var alias = await accounts.ResolveAliasAsync(account, cancellationToken);
            var authenticator = await accounts.GetAuthenticatorAsync(alias, cancellationToken);
            await authenticator.RevokeAsync(cancellationToken);

            return ToolResponse.Json(new
            {
                Provider = authenticator.ProviderName,
                Account = alias,
                Success = true,
                Message = $"OAuth token revoked and local tokens deleted for account '{alias}'. " +
                    "Run 'auth_status' to re-authenticate when ready.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
