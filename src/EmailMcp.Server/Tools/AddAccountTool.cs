using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class AddAccountTool
{
    [McpServerTool(Name = "add_account"), Description(
        "Adds an email account under a short alias you choose, and stores its Google OAuth " +
        "Client ID and Client Secret. Each account keeps its own credentials. " +
        "This does not sign in: run 'auth_status' with the same alias afterwards to open the " +
        "browser consent flow. The first account added becomes the default automatically.")]
    public static async Task<string> AddAccount(
        IAccountRegistry accounts,
        [Description("Short alias for this account, e.g. 'studio'. Lowercase letters, digits and hyphens only.")] string alias,
        [Description("Google OAuth Client ID (looks like: 123456789-abc.apps.googleusercontent.com)")] string clientId,
        [Description("Google OAuth Client Secret")] string clientSecret,
        [Description("Make this the default account used when no alias is given.")] bool setDefault = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.AddAccountAsync(alias, clientId, clientSecret, setDefault, cancellationToken);
            var normalized = AccountAlias.Normalize(alias);

            return ToolResponse.Json(new
            {
                Success = true,
                Account = normalized,
                Message = $"Account '{normalized}' added and credentials encrypted. " +
                    $"Run auth_status with account '{normalized}' to sign in.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
