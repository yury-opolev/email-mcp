using Google.Apis.Gmail.v1;

namespace EmailMcp.Gmail;

/// <summary>
/// Configuration options for the Gmail provider.
/// </summary>
public sealed class GmailOptions
{
    /// <summary>
    /// Path to the Google OAuth credentials JSON file.
    /// Defaults to ~/.email-mcp/credentials.json
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// OAuth scopes to request. Defaults to read + send + compose access.
    /// GmailCompose is what permits creating drafts; GmailSend alone does not.
    /// </summary>
    public string[] Scopes { get; set; } =
        [GmailService.Scope.GmailReadonly, GmailService.Scope.GmailSend, GmailService.Scope.GmailCompose];

    /// <summary>
    /// Application name sent to Google API.
    /// </summary>
    public string ApplicationName { get; set; } = "EmailMcp";
}
