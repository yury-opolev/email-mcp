using System.ComponentModel;
using System.Text.Json;
using EmailMcp.Abstractions;
using ModelContextProtocol.Server;

namespace EmailMcp.Server.Tools;

[McpServerToolType]
public static class RevokeAuthTool
{
    [McpServerTool(Name = "revoke_auth"), Description(
        "Fully revokes the OAuth token with Google and deletes all locally stored tokens. " +
        "Use this when you want to completely disconnect the app from the Google account. " +
        "After revoking, you will need to run 'auth_status' to re-authenticate. " +
        "This does NOT delete the stored client credentials (Client ID / Secret) — " +
        "use 'setup_gmail' if you need to change those.")]
    public static async Task<string> RevokeAuth(
        IEmailAuthenticator authenticator,
        CancellationToken cancellationToken = default)
    {
        await authenticator.RevokeAsync(cancellationToken);

        return JsonSerialize(new
        {
            Provider = authenticator.ProviderName,
            Success = true,
            Message = "OAuth token revoked and local tokens deleted. " +
                "Run 'auth_status' to re-authenticate when ready.",
        });
    }

    private static string JsonSerialize(object value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
