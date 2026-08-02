using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class AddAccountTool
{
    [McpServerTool(Name = "add_account"), Description(
        "Adds an email account under a short alias you choose. No credentials are needed: every " +
        "account uses the one OAuth client configured by 'setup_gmail'. " +
        "This does not sign in: run 'auth_status' with the same alias afterwards to open the " +
        "browser consent flow, signing in as the Google account you want bound to this alias. " +
        "The first account added becomes the default automatically.")]
    public static async Task<string> AddAccount(
        IAccountRegistry accounts,
        [Description("Short alias for this account, e.g. 'studio'. Lowercase letters, digits and hyphens only.")] string alias,
        [Description("Make this the default account used when no alias is given.")] bool setDefault = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.AddAccountAsync(alias, setDefault, cancellationToken);
            var normalized = AccountAlias.Normalize(alias);

            return ToolResponse.Json(new
            {
                Success = true,
                Account = normalized,
                Message = $"Account '{normalized}' added. " +
                    $"Run auth_status with account '{normalized}' to sign in.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
