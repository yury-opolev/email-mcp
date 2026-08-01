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
    /// Adds an account and stores its client credentials. Does not authenticate.
    /// </summary>
    /// <exception cref="AccountException">Thrown when the alias is invalid or already exists.</exception>
    Task AddAccountAsync(
        string alias,
        string clientId,
        string clientSecret,
        bool setDefault,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an account and its stored secrets. When <paramref name="revokeRemote"/> is true,
    /// the OAuth grant is revoked with the provider first.
    /// </summary>
    Task RemoveAccountAsync(string alias, bool revokeRemote, CancellationToken cancellationToken = default);

    /// <summary>Renames an account, re-keying its stored secrets.</summary>
    Task RenameAccountAsync(string alias, string newAlias, CancellationToken cancellationToken = default);

    /// <summary>Marks an account as the default.</summary>
    Task SetDefaultAccountAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the address the provider reports for an account, so that an alias bound to the
    /// wrong account becomes visible. Never throws: a failed lookup must not break authentication.
    /// </summary>
    Task TryRecordAddressAsync(string alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the client credentials for an account. The stored OAuth token is left alone and
    /// may no longer be valid.
    /// </summary>
    Task UpdateCredentialsAsync(
        string alias,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);
}
