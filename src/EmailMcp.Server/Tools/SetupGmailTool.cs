using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class SetupGmailTool
{
    [McpServerTool(Name = "setup_gmail"), Description(
        "Sets up Gmail credentials for the email MCP server. " +
        "Requires a Google OAuth Client ID and Client Secret from Google Cloud Console. " +
        "These values are encrypted and stored locally — they never leave your machine. " +
        "How to get these values: " +
        "1) Go to https://console.cloud.google.com " +
        "2) Create or select a project " +
        "3) Enable the Gmail API (APIs & Services → Library → search 'Gmail API' → Enable) " +
        "4) Configure OAuth consent screen (APIs & Services → OAuth consent screen → External → add your email as test user) " +
        "5) Create credentials (APIs & Services → Credentials → Create Credentials → OAuth client ID → Desktop app) " +
        "6) Copy the Client ID and Client Secret from the popup.")]
    public static async Task<string> SetupGmail(
        ITokenStore tokenStore,
        [Description("Google OAuth Client ID (looks like: 123456789-abc.apps.googleusercontent.com)")] string clientId,
        [Description("Google OAuth Client Secret")] string clientSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return JsonResult(false, "Client ID is required.");

        if (string.IsNullOrWhiteSpace(clientSecret))
            return JsonResult(false, "Client Secret is required.");

        if (!clientId.Contains(".apps.googleusercontent.com"))
            return JsonResult(false,
                "Client ID doesn't look right. It should end with '.apps.googleusercontent.com'. " +
                "Make sure you're using the OAuth Client ID, not the project ID.");

        var credentials = new
        {
            installed = new
            {
                client_id = clientId.Trim(),
                client_secret = clientSecret.Trim(),
                auth_uri = "https://accounts.google.com/o/oauth2/auth",
                token_uri = "https://oauth2.googleapis.com/token",
                redirect_uris = new[] { "http://localhost" },
            }
        };

        var json = JsonSerializer.Serialize(credentials);
        await tokenStore.SaveTokenAsync("gmail-client-credentials", json, cancellationToken);

        return JsonResult(true,
            "Gmail credentials saved and encrypted. " +
            "Now use the 'auth_status' tool to authenticate with your Google account. " +
            "This will open a browser window for you to sign in.");
    }

    private static string JsonResult(bool success, string message) =>
        JsonSerializer.Serialize(new
        {
            Success = success,
            Message = message,
        }, new JsonSerializerOptions { WriteIndented = true });
}
