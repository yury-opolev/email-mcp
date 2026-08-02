using System.ComponentModel;
using EmailMcp.Abstractions;
using EmailMcp.Gmail;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class AuthStatusTool
{
    [McpServerTool(Name = "auth_status"), Description(
        "Checks authentication status for one account. " +
        "If credentials are not configured, explains how to set them up. " +
        "If configured but not authenticated, initiates the OAuth flow which opens a browser for consent. " +
        "Run this tool first before using any other email tools.")]
    public static async Task<string> AuthStatus(
        IAccountRegistry accounts,
        [Description("Set to true to force re-authentication")] bool forceReauth = false,
        [Description(AccountParameter.Description)] string? account = null,
        CancellationToken cancellationToken = default)
    {
        string alias;
        IEmailAuthenticator authenticator;
        try
        {
            alias = await accounts.ResolveAliasAsync(account, cancellationToken);
            authenticator = await accounts.GetAuthenticatorAsync(alias, cancellationToken);
        }
        catch (AccountException ex)
        {
            return ToolResponse.Error(ex.Message);
        }

        if (authenticator is GmailAuthenticator gmailAuth)
        {
            var configured = await gmailAuth.AreCredentialsConfiguredAsync(cancellationToken);
            if (!configured)
            {
                return ToolResponse.Json(new
                {
                    Provider = authenticator.ProviderName,
                    Account = alias,
                    Status = "not_configured",
                    Message = $"Gmail credentials are not configured for account '{alias}'. " +
                        "Please follow these steps to set up Gmail API access:",
                    SetupInstructions = new[]
                    {
                        "1. Go to https://console.cloud.google.com/",
                        "2. Create a new project (or select an existing one) from the top dropdown",
                        "3. In the left menu, go to 'APIs & Services' -> 'Library'",
                        "4. Search for 'Gmail API' and click 'Enable'",
                        "5. Go to 'APIs & Services' -> 'OAuth consent screen'",
                        "6. Choose 'External' user type, click 'Create'",
                        "7. Fill in the App name (e.g. 'Email MCP'), your email, and save",
                        "8. On the 'Test users' page, click 'Add users' and add your Gmail address, then save",
                        "9. Go to 'APIs & Services' -> 'Credentials'",
                        "10. Click 'Create Credentials' -> 'OAuth client ID'",
                        "11. Choose 'Desktop app' as application type, give it a name, click 'Create'",
                        "12. Copy the 'Client ID' and 'Client Secret' shown in the popup",
                    },
                    NextStep = "Once you have the Client ID and Client Secret, use the 'setup_gmail' tool " +
                        "to provide them.",
                });
            }
        }

        if (forceReauth)
        {
            var reauthed = await authenticator.ReauthAsync(cancellationToken);
            if (reauthed)
            {
                await accounts.TryRecordAddressAsync(alias, cancellationToken);
            }

            return ToolResponse.Json(new
            {
                Provider = authenticator.ProviderName,
                Account = alias,
                Status = reauthed ? "authenticated" : "failed",
                Message = reauthed
                    ? $"Successfully re-authenticated account '{alias}'. You can now use email tools."
                    : "Re-authentication failed. Your credentials are still configured. " +
                      "Run 'auth_status' again to retry, or reconfigure them with 'setup_gmail'.",
            });
        }

        var isAuthenticated = await authenticator.IsAuthenticatedAsync(cancellationToken);

        if (!isAuthenticated)
        {
            var success = await authenticator.AuthenticateAsync(cancellationToken);
            if (success)
            {
                await accounts.TryRecordAddressAsync(alias, cancellationToken);
            }

            return ToolResponse.Json(new
            {
                Provider = authenticator.ProviderName,
                Account = alias,
                Status = success ? "authenticated" : "failed",
                Message = success
                    ? $"Successfully authenticated account '{alias}'. You can now use email tools."
                    : "Authentication failed. Your credentials are still configured. " +
                      "Run 'auth_status' again to retry, or use 'auth_status' with forceReauth to start a fresh session.",
            });
        }

        await accounts.TryRecordAddressAsync(alias, cancellationToken);

        return ToolResponse.Json(new
        {
            Provider = authenticator.ProviderName,
            Account = alias,
            Status = "authenticated",
            Message = $"Account '{alias}' is already authenticated. Email tools are ready to use.",
        });
    }
}
