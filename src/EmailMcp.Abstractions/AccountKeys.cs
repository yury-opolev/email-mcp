namespace EmailMcp.Abstractions;

/// <summary>
/// Builds token-store keys for per-account secrets.
/// </summary>
/// <remarks>
/// The separator is "--" rather than ":" on purpose. Token-store keys become filenames, and the
/// store replaces characters that are invalid in filenames. ":" is invalid on Windows, so
/// "account:studio:token" would land on disk as "account_studio_token" - which is only
/// unambiguous while the alias charset happens to exclude "_". "--" survives sanitisation
/// unchanged, so key isolation does not depend on the validation rule staying as it is.
/// </remarks>
public static class AccountKeys
{
    /// <summary>Key holding the JSON account index.</summary>
    public const string Index = "accounts--index";

    /// <summary>Legacy key for client credentials, written before multi-account support.</summary>
    public const string LegacyClientCredentials = "gmail-client-credentials";

    /// <summary>Legacy key for the OAuth token, written before multi-account support.</summary>
    public const string LegacyOAuthToken = "gmail-oauth-token-user";

    /// <summary>Alias assigned to a migrated legacy account.</summary>
    public const string LegacyAlias = "default";

    /// <summary>Key holding the client ID and secret for one account.</summary>
    public static string ClientCredentials(string alias) => $"account--{alias}--client-credentials";

    /// <summary>
    /// Key holding the OAuth token for one account. The "user" suffix matches the user id passed
    /// to Google's authorisation broker, which the broker uses when it names the stored token.
    /// </summary>
    public static string OAuthToken(string alias) => $"account--{alias}--oauth-token-user";

    /// <summary>
    /// Prefix used by the Google IDataStore adapter when it normalises its own keys for an
    /// account, so that tokens written by the broker land under the account's namespace.
    /// </summary>
    public static string OAuthTokenPrefix(string alias) => $"account--{alias}--oauth-token";
}
