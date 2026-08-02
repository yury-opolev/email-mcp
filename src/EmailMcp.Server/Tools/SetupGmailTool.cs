using System.ComponentModel;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class SetupGmailTool
{
    [McpServerTool(Name = "setup_gmail"), Description(
        "Stores the Google OAuth Client ID and Client Secret used by every account. " +
        "One OAuth client authorises any number of Google accounts, so this is set once, " +
        "not per account. With no accounts configured this also creates one called 'default'. " +
        "Running it again rotates the client for every account. " +
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
            var result = await accounts.SetSharedCredentialsAsync(clientId, clientSecret, cancellationToken);

            if (existing.Count == 0)
            {
                await accounts.AddAccountAsync(AccountKeys.LegacyAlias, setDefault: true, cancellationToken);

                return ToolResponse.Json(new
                {
                    Success = true,
                    Account = AccountKeys.LegacyAlias,
                    Message = "Credentials saved and encrypted, and the 'default' account was created. " +
                        "Now use the 'auth_status' tool to authenticate with your Google account. " +
                        "This will open a browser window for you to sign in.",
                });
            }

            if (result.ClientIdChanged && result.AuthenticatedAccounts.Count > 0)
            {
                return ToolResponse.Json(new
                {
                    Success = true,
                    ClientIdChanged = true,
                    NeedsReauthentication = result.AuthenticatedAccounts,
                    Message = "Credentials replaced for every account. The Client ID changed, so the " +
                        "stored sign-in for these accounts is no longer valid: " +
                        string.Join(", ", result.AuthenticatedAccounts) +
                        ". Run 'auth_status' for each of them to sign in again.",
                });
            }

            return ToolResponse.Json(new
            {
                Success = true,
                ClientIdChanged = result.ClientIdChanged,
                Message = "Credentials saved and encrypted. They apply to every configured account.",
            });
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
