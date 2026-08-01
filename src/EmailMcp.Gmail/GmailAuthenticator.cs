using System.Text.Json;
using EmailMcp.Abstractions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace EmailMcp.Gmail;

/// <summary>
/// Handles Gmail OAuth 2.0 authentication for a single account, using Google's authorization
/// code flow. Tokens are persisted via ITokenStore with encryption, namespaced by account alias.
/// </summary>
public sealed class GmailAuthenticator : IEmailAuthenticator
{
    private const string UserId = "user";

    private readonly ITokenStore tokenStore;
    private readonly GmailOptions options;
    private readonly ILogger<GmailAuthenticator> logger;
    private readonly string accountAlias;
    private readonly string oauthTokenKey;
    private readonly string clientCredentialsKey;

    private UserCredential? credential;

    public string ProviderName => "Gmail";

    /// <summary>The account alias this authenticator is bound to.</summary>
    public string AccountAlias => this.accountAlias;

    public GmailAuthenticator(
        ITokenStore tokenStore,
        GmailOptions options,
        ILogger<GmailAuthenticator> logger,
        string accountAlias)
    {
        this.tokenStore = tokenStore;
        this.options = options;
        this.logger = logger;
        this.accountAlias = accountAlias;
        this.oauthTokenKey = AccountKeys.OAuthToken(accountAlias);
        this.clientCredentialsKey = AccountKeys.ClientCredentials(accountAlias);
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (this.credential is not null)
        {
            return true;
        }

        return await this.tokenStore.ExistsAsync(this.oauthTokenKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns true if client credentials (Client ID + Secret) have been configured for this account.
    /// </summary>
    public async Task<bool> AreCredentialsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        if (await this.tokenStore.ExistsAsync(this.clientCredentialsKey, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return File.Exists(this.ResolveCredentialsPath());
    }

    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var clientSecrets = await this.LoadClientSecretsAsync(cancellationToken).ConfigureAwait(false);
            var dataStore = new EncryptedDataStore(this.tokenStore, this.accountAlias);

            this.credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                this.options.Scopes,
                UserId,
                cancellationToken,
                dataStore).ConfigureAwait(false);

            if (this.credential.Token.IsStale)
            {
                await this.credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            }

            this.logger.LogInformation(
                "Gmail authentication successful for account '{Alias}'",
                this.accountAlias);
            return true;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Gmail authentication failed for account '{Alias}'", this.accountAlias);
            return false;
        }
    }

    public async Task<bool> ReauthAsync(CancellationToken cancellationToken = default)
    {
        this.credential = null;

        await this.tokenStore.DeleteTokenAsync(this.oauthTokenKey, cancellationToken).ConfigureAwait(false);

        this.logger.LogInformation(
            "Cleared cached Gmail OAuth token for account '{Alias}', re-authenticating",
            this.accountAlias);

        return await this.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAsync(CancellationToken cancellationToken = default)
    {
        if (this.credential is not null)
        {
            try
            {
                await this.credential.RevokeTokenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(
                    ex,
                    "Failed to revoke token with Google for account '{Alias}'; clearing local tokens anyway",
                    this.accountAlias);
            }
            finally
            {
                this.credential = null;
            }
        }

        await this.tokenStore.DeleteTokenAsync(this.oauthTokenKey, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation(
            "Gmail credentials revoked and local tokens deleted for account '{Alias}'",
            this.accountAlias);
    }

    /// <summary>
    /// Deletes this account's stored client credentials. Used when removing an account.
    /// </summary>
    internal async Task DeleteClientCredentialsAsync(CancellationToken cancellationToken = default)
    {
        await this.tokenStore.DeleteTokenAsync(this.clientCredentialsKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks Gmail which address this account is actually signed in as.
    /// Returns null if the lookup fails; callers must treat that as non-fatal.
    /// </summary>
    internal async Task<string?> TryGetAddressAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var authenticatedCredential = await this.GetCredentialAsync(cancellationToken).ConfigureAwait(false);
            using var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = authenticatedCredential,
                ApplicationName = this.options.ApplicationName,
            });

            var profile = await service.Users.GetProfile("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return profile.EmailAddress;
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(
                ex,
                "Could not read the Gmail address for account '{Alias}'",
                this.accountAlias);
            return null;
        }
    }

    /// <summary>
    /// Returns the authenticated credential, authenticating if necessary.
    /// </summary>
    internal async Task<UserCredential> GetCredentialAsync(CancellationToken cancellationToken = default)
    {
        if (this.credential is null)
        {
            var authenticated = await this.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
            if (!authenticated || this.credential is null)
            {
                throw new InvalidOperationException(
                    $"Gmail authentication required for account '{this.accountAlias}'. " +
                    "Please run the auth_status tool first.");
            }
        }

        if (this.credential.Token.IsStale)
        {
            await this.credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        }

        return this.credential;
    }

    private string ResolveCredentialsPath() =>
        this.options.CredentialsPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".email-mcp",
                "credentials.json");

    private async Task<ClientSecrets> LoadClientSecretsAsync(CancellationToken cancellationToken)
    {
        var storedCredentials = await this.tokenStore
            .LoadTokenAsync(this.clientCredentialsKey, cancellationToken)
            .ConfigureAwait(false);

        if (storedCredentials is not null)
        {
            this.logger.LogDebug(
                "Loading Gmail credentials for account '{Alias}' from encrypted store",
                this.accountAlias);
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(storedCredentials));
            var secrets = await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            return secrets.Secrets;
        }

        var credentialsPath = this.ResolveCredentialsPath();

        if (!File.Exists(credentialsPath))
        {
            throw new FileNotFoundException(
                $"Gmail credentials are not configured for account '{this.accountAlias}'. " +
                "Use the 'add_account' tool to provide a Google OAuth Client ID and Client Secret, " +
                "or place a credentials.json file at: " + credentialsPath);
        }

        await using var fileStream = File.OpenRead(credentialsPath);
        var fileSecrets = await GoogleClientSecrets.FromStreamAsync(fileStream, cancellationToken).ConfigureAwait(false);
        return fileSecrets.Secrets;
    }

    /// <summary>
    /// Adapter that bridges Google's IDataStore to the encrypted ITokenStore, namespaced per account.
    /// </summary>
    private sealed class EncryptedDataStore : IDataStore
    {
        private readonly ITokenStore tokenStore;
        private readonly string keyPrefix;

        public EncryptedDataStore(ITokenStore tokenStore, string accountAlias)
        {
            this.tokenStore = tokenStore;
            this.keyPrefix = AccountKeys.OAuthTokenPrefix(accountAlias);
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            await this.tokenStore.SaveTokenAsync(this.NormalizeKey(key), json).ConfigureAwait(false);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var json = await this.tokenStore.LoadTokenAsync(this.NormalizeKey(key)).ConfigureAwait(false);
            if (json is null)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task DeleteAsync<T>(string key)
        {
            await this.tokenStore.DeleteTokenAsync(this.NormalizeKey(key)).ConfigureAwait(false);
        }

        public async Task ClearAsync()
        {
            await this.tokenStore.DeleteTokenAsync(this.NormalizeKey(UserId)).ConfigureAwait(false);
        }

        private string NormalizeKey(string key) => $"{this.keyPrefix}-{key}";
    }
}
