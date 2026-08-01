using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class RenameAccountTool
{
    [McpServerTool(Name = "rename_account"), Description(
        "Renames an account, moving its stored credentials and sign-in with it. " +
        "The sign-in is preserved, so there is no need to re-authenticate. " +
        "If the renamed account was the default, it stays the default under its new name.")]
    public static async Task<string> RenameAccount(
        IAccountRegistry accounts,
        [Description("Current alias of the account.")] string alias,
        [Description("New alias. Lowercase letters, digits and hyphens only.")] string newAlias,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.RenameAccountAsync(alias, newAlias, cancellationToken);

            return ToolResponse.Json(new
            {
                Success = true,
                From = AccountAlias.Normalize(alias),
                To = AccountAlias.Normalize(newAlias),
                Message = "Account renamed. Its sign-in and credentials moved with it.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
