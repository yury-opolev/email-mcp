using System.Text.Json;
using EmailMcp.Abstractions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace EmailMcp.Gmail;

/// <summary>
/// Handles Gmail OAuth 2.0 authentication using Google's authorization code flow.
/// Tokens are persisted via ITokenStore with encryption.
/// </summary>
public sealed class GmailAuthenticator : IEmailAuthenticator
{
    private const string TokenKey = "gmail-oauth-token";
    private const string ClientCredentialsKey = "gmail-client-credentials";
    private const string UserId = "user";

    private readonly ITokenStore _tokenStore;
    private readonly GmailOptions _options;
    private readonly ILogger<GmailAuthenticator> _logger;

    private UserCredential? _credential;

    public string ProviderName => "Gmail";

    public GmailAuthenticator(
        ITokenStore tokenStore,
        GmailOptions options,
        ILogger<GmailAuthenticator> logger)
    {
        _tokenStore = tokenStore;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (_credential is not null)
            return true;

        return await _tokenStore.ExistsAsync(TokenKey, cancellationToken);
    }

    /// <summary>
    /// Returns true if client credentials (Client ID + Secret) have been configured.
    /// </summary>
    public async Task<bool> AreCredentialsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        // Check encrypted store first, then file
        if (await _tokenStore.ExistsAsync(ClientCredentialsKey, cancellationToken))
            return true;

        var credentialsPath = _options.CredentialsPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".email-mcp",
                "credentials.json");

        return File.Exists(credentialsPath);
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var clientSecrets = await LoadClientSecretsAsync(cancellationToken);
            var tokenStore = new EncryptedDataStore(_tokenStore);

            _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                _options.Scopes,
                UserId,
                cancellationToken,
                tokenStore);

            if (_credential.Token.IsStale)
            {
                await _credential.RefreshTokenAsync(cancellationToken);
            }

            _logger.LogInformation("Gmail authentication successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gmail authentication failed");
            return false;
        }
    }

    public async Task<bool> ReauthAsync(CancellationToken cancellationToken = default)
    {
        // Clear in-memory credential
        _credential = null;

        // Clear persisted OAuth token (without contacting Google to revoke it)
        await _tokenStore.DeleteTokenAsync(TokenKey, cancellationToken);
        await _tokenStore.DeleteTokenAsync($"{TokenKey}-{UserId}", cancellationToken);

        _logger.LogInformation("Cleared cached Gmail OAuth token, re-authenticating");

        return await AuthenticateAsync(cancellationToken);
    }

    public async Task RevokeAsync(CancellationToken cancellationToken = default)
    {
        if (_credential is not null)
        {
            try
            {
                await _credential.RevokeTokenAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to revoke token with Google; clearing local tokens anyway");
            }
            finally
            {
                _credential = null;
            }
        }

        await _tokenStore.DeleteTokenAsync(TokenKey, cancellationToken);
        await _tokenStore.DeleteTokenAsync($"{TokenKey}-{UserId}", cancellationToken);
        _logger.LogInformation("Gmail credentials revoked and local tokens deleted");
    }

    /// <summary>
    /// Returns the authenticated credential, authenticating if necessary.
    /// </summary>
    internal async Task<UserCredential> GetCredentialAsync(CancellationToken cancellationToken = default)
    {
        if (_credential is null)
        {
            var authenticated = await AuthenticateAsync(cancellationToken);
            if (!authenticated || _credential is null)
                throw new InvalidOperationException(
                    "Gmail authentication required. Please run the auth_status tool first.");
        }

        if (_credential.Token.IsStale)
        {
            await _credential.RefreshTokenAsync(cancellationToken);
        }

        return _credential;
    }

    private async Task<ClientSecrets> LoadClientSecretsAsync(CancellationToken cancellationToken)
    {
        // Try loading from encrypted token store first (set up via setup_gmail tool)
        var storedCredentials = await _tokenStore.LoadTokenAsync(ClientCredentialsKey, cancellationToken);
        if (storedCredentials is not null)
        {
            _logger.LogDebug("Loading Gmail credentials from encrypted store");
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(storedCredentials));
            var secrets = await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken);
            return secrets.Secrets;
        }

        // Fallback to credentials.json file
        var credentialsPath = _options.CredentialsPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".email-mcp",
                "credentials.json");

        if (!File.Exists(credentialsPath))
            throw new FileNotFoundException(
                "Gmail credentials not configured. " +
                "Please use the 'setup_gmail' tool to provide your Google OAuth Client ID and Client Secret, " +
                "or place a credentials.json file at: " + credentialsPath);

        await using var fileStream = File.OpenRead(credentialsPath);
        var fileSecrets = await GoogleClientSecrets.FromStreamAsync(fileStream, cancellationToken);
        return fileSecrets.Secrets;
    }

    /// <summary>
    /// Adapter that bridges Google's IDataStore to our encrypted ITokenStore.
    /// </summary>
    private sealed class EncryptedDataStore : IDataStore
    {
        private readonly ITokenStore _tokenStore;

        public EncryptedDataStore(ITokenStore tokenStore)
        {
            _tokenStore = tokenStore;
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            await _tokenStore.SaveTokenAsync(NormalizeKey(key), json);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var json = await _tokenStore.LoadTokenAsync(NormalizeKey(key));
            if (json is null)
                return default;

            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task DeleteAsync<T>(string key)
        {
            await _tokenStore.DeleteTokenAsync(NormalizeKey(key));
        }

        public async Task ClearAsync()
        {
            await _tokenStore.DeleteTokenAsync(TokenKey);
        }

        private static string NormalizeKey(string key) => $"{TokenKey}-{key}";
    }
}
