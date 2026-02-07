namespace EmailMcp.Abstractions;

/// <summary>
/// Persists and retrieves sensitive token data with encryption.
/// </summary>
public interface ITokenStore
{
    /// <summary>
    /// Saves token data, encrypting it before writing to storage.
    /// </summary>
    /// <param name="key">A unique key identifying the token (e.g., "gmail-oauth").</param>
    /// <param name="tokenData">The raw token data to encrypt and store.</param>
    Task SaveTokenAsync(string key, string tokenData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads and decrypts token data.
    /// Returns null if no token exists for the given key.
    /// </summary>
    Task<string?> LoadTokenAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes stored token data for the given key.
    /// </summary>
    Task DeleteTokenAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a token exists for the given key.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
