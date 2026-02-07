using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Gmail;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class AuthStatusTool
{
    [McpServerTool(Name = "auth_status"), Description(
        "Checks authentication status for the email provider. " +
        "If not configured, instructs the user to run 'setup_gmail' first. " +
        "If configured but not authenticated, initiates the OAuth flow which opens a browser for consent. " +
        "Run this tool first before using any other email tools.")]
    public static async Task<string> AuthStatus(
        IEmailAuthenticator authenticator,
        [Description("Set to true to force re-authentication")] bool forceReauth = false,
        CancellationToken cancellationToken = default)
    {
        // Check if credentials are configured
        if (authenticator is GmailAuthenticator gmailAuth)
        {
            var configured = await gmailAuth.AreCredentialsConfiguredAsync(cancellationToken);
            if (!configured)
            {
                return JsonSerialize(new
                {
                    Provider = authenticator.ProviderName,
                    Status = "not_configured",
                    Message = "Gmail credentials are not configured. " +
                        "Please follow these steps to set up Gmail API access:",
                    SetupInstructions = new[]
                    {
                        "1. Go to https://console.cloud.google.com/",
                        "2. Create a new project (or select an existing one) from the top dropdown",
                        "3. In the left menu, go to 'APIs & Services' → 'Library'",
                        "4. Search for 'Gmail API' and click 'Enable'",
                        "5. Go to 'APIs & Services' → 'OAuth consent screen'",
                        "6. Choose 'External' user type, click 'Create'",
                        "7. Fill in the App name (e.g. 'Email MCP'), your email, and save",
                        "8. On the 'Test users' page, click 'Add users' and add your Gmail address, then save",
                        "9. Go to 'APIs & Services' → 'Credentials'",
                        "10. Click 'Create Credentials' → 'OAuth client ID'",
                        "11. Choose 'Desktop app' as application type, give it a name, click 'Create'",
                        "12. Copy the 'Client ID' and 'Client Secret' shown in the popup",
                    },
                    NextStep = "Once you have the Client ID and Client Secret, use the 'setup_gmail' tool to provide them.",
                });
            }
        }

        if (forceReauth)
        {
            await authenticator.RevokeAsync(cancellationToken);
        }

        var isAuthenticated = await authenticator.IsAuthenticatedAsync(cancellationToken);

        if (!isAuthenticated)
        {
            var success = await authenticator.AuthenticateAsync(cancellationToken);
            return JsonSerialize(new
            {
                Provider = authenticator.ProviderName,
                Status = success ? "authenticated" : "failed",
                Message = success
                    ? "Successfully authenticated. You can now use email tools."
                    : "Authentication failed. Please check your credentials and try again.",
            });
        }

        return JsonSerialize(new
        {
            Provider = authenticator.ProviderName,
            Status = "authenticated",
            Message = "Already authenticated. Email tools are ready to use.",
        });
    }

    private static string JsonSerialize(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
