using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class SetupGmailTool
{
    [McpServerTool(Name = "setup_gmail"), Description(
        "Sets up Gmail credentials for a single-account setup. " +
        "With no accounts configured this creates one called 'default'; with exactly one account " +
        "configured it replaces that account's credentials. If several accounts exist, use " +
        "'add_account' or 'update_account_credentials' instead so it is clear which one you mean. " +
        "Requires a Google OAuth Client ID and Client Secret from Google Cloud Console. " +
        "These values are encrypted and stored locally - they never leave your machine. " +
        "How to get these values: " +
        "1) Go to https://console.cloud.google.com " +
        "2) Create or select a project " +
        "3) Enable the Gmail API (APIs & Services -> Library -> search 'Gmail API' -> Enable) " +
        "4) Configure OAuth consent screen (APIs & Services -> OAuth consent screen -> External -> add your email as test user) " +
        "5) Create credentials (APIs & Services -> Credentials -> Create Credentials -> OAuth client ID -> Desktop app) " +
        "6) Copy the Client ID and Client Secret from the popup.")]
    public static async Task<string> SetupGmail(
        IAccountRegistry accounts,
        [Description("Google OAuth Client ID (looks like: 123456789-abc.apps.googleusercontent.com)")] string clientId,
        [Description("Google OAuth Client Secret")] string clientSecret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await accounts.ListAccountsAsync(cancellationToken);

            switch (existing.Count)
            {
                case 0:
                    await accounts.AddAccountAsync(
                        AccountKeys.LegacyAlias,
                        clientId,
                        clientSecret,
                        setDefault: true,
                        cancellationToken);

                    return ToolResponse.Json(new
                    {
                        Success = true,
                        Account = AccountKeys.LegacyAlias,
                        Message = "Gmail credentials saved and encrypted for the 'default' account. " +
                            "Now use the 'auth_status' tool to authenticate with your Google account. " +
                            "This will open a browser window for you to sign in.",
                    });

                case 1:
                    var alias = existing[0].Alias;
                    await accounts.UpdateCredentialsAsync(alias, clientId, clientSecret, cancellationToken);

                    return ToolResponse.Json(new
                    {
                        Success = true,
                        Account = alias,
                        Message = $"Gmail credentials replaced for account '{alias}'. " +
                            "The previously stored sign-in may no longer be valid; " +
                            "run 'auth_status' to re-authenticate.",
                    });

                default:
                    return ToolResponse.Error(
                        "Several accounts are configured, so 'setup_gmail' cannot tell which one you mean. " +
                        "Use 'add_account' to create a new one, or 'update_account_credentials' to change " +
                        "an existing one. Known accounts: " +
                        string.Join(", ", existing.Select(a => a.Alias)) + ".");
            }
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
