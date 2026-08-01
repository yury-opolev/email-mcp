using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class UpdateAccountCredentialsTool
{
    [McpServerTool(Name = "update_account_credentials"), Description(
        "Replaces the Google OAuth Client ID and Client Secret stored for one account. " +
        "The existing sign-in is left in place but may no longer be valid, since it was granted " +
        "against the old client; run 'auth_status' afterwards to check, and re-authenticate if needed.")]
    public static async Task<string> UpdateAccountCredentials(
        IAccountRegistry accounts,
        [Description("Alias of the account to update.")] string alias,
        [Description("Google OAuth Client ID (looks like: 123456789-abc.apps.googleusercontent.com)")] string clientId,
        [Description("Google OAuth Client Secret")] string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.UpdateCredentialsAsync(alias, clientId, clientSecret, cancellationToken);
            var normalized = AccountAlias.Normalize(alias);

            return ToolResponse.Json(new
            {
                Success = true,
                Account = normalized,
                Message = $"Credentials replaced for account '{normalized}'. " +
                    "The stored sign-in may no longer be valid; run 'auth_status' to check.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
