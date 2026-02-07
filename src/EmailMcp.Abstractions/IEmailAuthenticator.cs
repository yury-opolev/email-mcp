namespace EmailMcp.Abstractions;

/// <summary>
/// Handles authentication for an email provider.
/// </summary>
public interface IEmailAuthenticator
{
    /// <summary>
    /// Gets the provider name this authenticator is for.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Returns true if valid credentials are available.
    /// </summary>
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates authentication flow (e.g., opens browser for OAuth consent).
    /// Returns true if authentication succeeded.
    /// </summary>
    Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes stored credentials.
    /// </summary>
    Task RevokeAsync(CancellationToken cancellationToken = default);
}
