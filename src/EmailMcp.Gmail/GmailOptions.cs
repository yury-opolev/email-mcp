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
    /// OAuth scopes to request. Defaults to read-only access.
    /// </summary>
    public string[] Scopes { get; set; } = [GmailService.Scope.GmailReadonly];

    /// <summary>
    /// Application name sent to Google API.
    /// </summary>
    public string ApplicationName { get; set; } = "EmailMcp";
}
