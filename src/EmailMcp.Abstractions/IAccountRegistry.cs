namespace EmailMcp.Abstractions;

/// <summary>
/// Owns the set of configured accounts and hands out a provider or authenticator for each.
/// </summary>
/// <remarks>
/// Implementations cache one provider and one authenticator per alias, because authenticators
/// hold a credential in memory and re-creating them would discard it.
/// </remarks>
public interface IAccountRegistry
{
    /// <summary>Lists configured accounts, in the order they were added.</summary>
    Task<IReadOnlyList<EmailAccount>> ListAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the alias to use for an operation.
    /// </summary>
    /// <param name="requested">
    /// The alias asked for, or null to use the default.
    /// </param>
    /// <exception cref="AccountException">
    /// Thrown when the requested alias is unknown, when no accounts are configured, or when
    /// several accounts exist and none was requested or marked default.
    /// </exception>
    Task<string> ResolveAliasAsync(string? requested, CancellationToken cancellationToken = default);

    /// <summary>Returns true if the named account has a stored OAuth token.</summary>
    Task<bool> IsAuthenticatedAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>Gets the email provider for an account, resolving the alias first.</summary>
    Task<IEmailProvider> GetProviderAsync(string? requested, CancellationToken cancellationToken = default);

    /// <summary>Gets the authenticator for an account, resolving the alias first.</summary>
    Task<IEmailAuthenticator> GetAuthenticatorAsync(string? requested, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an account. Does not authenticate, and does not take credentials: every account uses
    /// the shared client.
    /// </summary>
    /// <exception cref="AccountException">
    /// Thrown when the alias is invalid, already exists, or no shared credentials are configured.
    /// </exception>
    Task AddAccountAsync(string alias, bool setDefault, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an account and its stored OAuth token. The shared client credentials are untouched.
    /// When <paramref name="revokeRemote"/> is true, the OAuth grant is revoked with the provider
    /// first.
    /// </summary>
    Task RemoveAccountAsync(string alias, bool revokeRemote, CancellationToken cancellationToken = default);

    /// <summary>Renames an account, moving its stored OAuth token with it.</summary>
    Task RenameAccountAsync(string alias, string newAlias, CancellationToken cancellationToken = default);

    /// <summary>Marks an account as the default.</summary>
    Task SetDefaultAccountAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the address the provider reports for an account, so that an alias bound to the
    /// wrong account becomes visible. Never throws: a failed lookup must not break authentication.
    /// </summary>
    Task TryRecordAddressAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>Returns true once a shared Client ID and Secret are available.</summary>
    Task<bool> AreSharedCredentialsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the client credentials shared by every account. Stored OAuth tokens are left
    /// alone; the result says which accounts a changed Client ID has invalidated.
    /// </summary>
    Task<CredentialUpdateResult> SetSharedCredentialsAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);
}
