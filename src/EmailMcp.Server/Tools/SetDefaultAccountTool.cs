using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class SetDefaultAccountTool
{
    [McpServerTool(Name = "set_default_account"), Description(
        "Chooses which account is used when a tool is called without an 'account' argument. " +
        "Only matters when more than one account is configured: with exactly one, that account " +
        "is always used regardless of this setting.")]
    public static async Task<string> SetDefaultAccount(
        IAccountRegistry accounts,
        [Description("Alias of the account to make default.")] string alias,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await accounts.SetDefaultAccountAsync(alias, cancellationToken);
            var normalized = AccountAlias.Normalize(alias);

            return ToolResponse.Json(new
            {
                Success = true,
                Default = normalized,
                Message = $"Account '{normalized}' is now the default.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
