namespace EmailMcp.Gmail;

/// <summary>
/// Resolves the credentials.json fallback location, used when no credentials are in the token
/// store. Shared so the authenticator and the registry cannot disagree about where it lives.
/// </summary>
internal static class GmailCredentialsPath
{
    public static string Resolve(GmailOptions options) =>
        options.CredentialsPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".email-mcp",
                "credentials.json");
}
