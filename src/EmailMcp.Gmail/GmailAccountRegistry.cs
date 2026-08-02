using System.Collections.Concurrent;
using System.Text.Json;
using EmailMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace EmailMcp.Gmail;

/// <summary>
/// Owns the configured Gmail accounts and hands out a cached authenticator and provider per alias.
/// </summary>
public sealed class GmailAccountRegistry : IAccountRegistry
{
    private readonly ITokenStore tokenStore;
    private readonly AccountIndexStore indexStore;
    private readonly GmailOptions options;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<GmailAccountRegistry> logger;

    private readonly ConcurrentDictionary<string, GmailAuthenticator> authenticators = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GmailEmailProvider> providers = new(StringComparer.Ordinal);

    public GmailAccountRegistry(
        ITokenStore tokenStore,
        AccountIndexStore indexStore,
        GmailOptions options,
        ILoggerFactory loggerFactory,
        ILogger<GmailAccountRegistry> logger)
    {
        this.tokenStore = tokenStore;
        this.indexStore = indexStore;
        this.options = options;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<EmailAccount>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return index.Accounts;
    }

    public async Task<string> ResolveAliasAsync(string? requested, CancellationToken cancellationToken = default)
    {
        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return ResolveAlias(index, requested);
    }

    public async Task<bool> IsAuthenticatedAsync(string alias, CancellationToken cancellationToken = default)
    {
        return await this.tokenStore
            .ExistsAsync(AccountKeys.OAuthToken(alias), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IEmailProvider> GetProviderAsync(
        string? requested,
        CancellationToken cancellationToken = default)
    {
        var alias = await this.ResolveAliasAsync(requested, cancellationToken).ConfigureAwait(false);
        return this.GetProvider(alias);
    }

    public async Task<IEmailAuthenticator> GetAuthenticatorAsync(
        string? requested,
        CancellationToken cancellationToken = default)
    {
        var alias = await this.ResolveAliasAsync(requested, cancellationToken).ConfigureAwait(false);
        return this.GetAuthenticator(alias);
    }

    public async Task AddAccountAsync(
        string alias,
        string clientId,
        string clientSecret,
        bool setDefault,
        CancellationToken cancellationToken = default)
    {
        var normalized = RequireValidAlias(alias);
        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (index.Accounts.Any(a => a.Alias == normalized))
        {
            throw new AccountException(
                $"An account named '{normalized}' already exists. " +
                "Use 'update_account_credentials' to change its credentials, or pick another alias.");
        }

        await this.StoreClientCredentialsAsync(normalized, clientId, clientSecret, cancellationToken)
            .ConfigureAwait(false);

        index.Accounts.Add(new EmailAccount(normalized, EmailAddress: null, DateTimeOffset.UtcNow));

        if (setDefault || index.Accounts.Count == 1)
        {
            index.DefaultAlias = normalized;
        }

        await this.indexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Added account '{Alias}'", normalized);
    }

    public async Task RemoveAccountAsync(
        string alias,
        bool revokeRemote,
        CancellationToken cancellationToken = default)
    {
        var normalized = AccountAlias.Normalize(alias);
        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var account = RequireExisting(index, normalized);

        if (revokeRemote)
        {
            var authenticator = this.GetAuthenticator(normalized);
            await authenticator.RevokeAsync(cancellationToken).ConfigureAwait(false);
        }

        await this.tokenStore.DeleteTokenAsync(AccountKeys.OAuthToken(normalized), cancellationToken)
            .ConfigureAwait(false);
        await this.tokenStore.DeleteTokenAsync(AccountKeys.LegacyAccountClientCredentials(normalized), cancellationToken)
            .ConfigureAwait(false);

        index.Accounts.Remove(account);
        this.authenticators.TryRemove(normalized, out _);
        this.providers.TryRemove(normalized, out _);

        if (index.DefaultAlias == normalized)
        {
            index.DefaultAlias = index.Accounts.Count == 1 ? index.Accounts[0].Alias : null;
        }

        await this.indexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Removed account '{Alias}'", normalized);
    }

    public async Task RenameAccountAsync(
        string alias,
        string newAlias,
        CancellationToken cancellationToken = default)
    {
        var from = AccountAlias.Normalize(alias);
        var to = RequireValidAlias(newAlias);

        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var account = RequireExisting(index, from);

        if (index.Accounts.Any(a => a.Alias == to))
        {
            throw new AccountException($"An account named '{to}' already exists.");
        }

        await this.MoveTokenAsync(
            AccountKeys.LegacyAccountClientCredentials(from),
            AccountKeys.LegacyAccountClientCredentials(to),
            cancellationToken).ConfigureAwait(false);

        await this.MoveTokenAsync(
            AccountKeys.OAuthToken(from),
            AccountKeys.OAuthToken(to),
            cancellationToken).ConfigureAwait(false);

        var position = index.Accounts.IndexOf(account);
        index.Accounts[position] = account with { Alias = to };

        if (index.DefaultAlias == from)
        {
            index.DefaultAlias = to;
        }

        this.authenticators.TryRemove(from, out _);
        this.providers.TryRemove(from, out _);

        await this.indexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Renamed account '{From}' to '{To}'", from, to);
    }

    public async Task SetDefaultAccountAsync(string alias, CancellationToken cancellationToken = default)
    {
        var normalized = AccountAlias.Normalize(alias);
        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        RequireExisting(index, normalized);

        index.DefaultAlias = normalized;
        await this.indexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateCredentialsAsync(
        string alias,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        var normalized = AccountAlias.Normalize(alias);
        var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        RequireExisting(index, normalized);

        await this.StoreClientCredentialsAsync(normalized, clientId, clientSecret, cancellationToken)
            .ConfigureAwait(false);

        this.authenticators.TryRemove(normalized, out _);
        this.providers.TryRemove(normalized, out _);
    }

    /// <summary>
    /// Records the address Gmail reports for an account, so a mis-bound alias is visible.
    /// Never throws: a failed lookup must not break authentication.
    /// </summary>
    public async Task TryRecordAddressAsync(string alias, CancellationToken cancellationToken = default)
    {
        try
        {
            var authenticator = this.GetAuthenticator(alias);
            var address = await authenticator.TryGetAddressAsync(cancellationToken).ConfigureAwait(false);
            if (address is null)
            {
                return;
            }

            var index = await this.indexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var account = index.Accounts.FirstOrDefault(a => a.Alias == alias);
            if (account is null || account.EmailAddress == address)
            {
                return;
            }

            var position = index.Accounts.IndexOf(account);
            index.Accounts[position] = account with { EmailAddress = address };
            await this.indexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not record the address for account '{Alias}'", alias);
        }
    }

    internal GmailAuthenticator GetAuthenticator(string alias) =>
        this.authenticators.GetOrAdd(alias, key => new GmailAuthenticator(
            this.tokenStore,
            this.options,
            this.loggerFactory.CreateLogger<GmailAuthenticator>(),
            key));

    private GmailEmailProvider GetProvider(string alias) =>
        this.providers.GetOrAdd(alias, key => new GmailEmailProvider(
            this.GetAuthenticator(key),
            this.options,
            this.loggerFactory.CreateLogger<GmailEmailProvider>()));

    private static string ResolveAlias(AccountIndex index, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var normalized = AccountAlias.Normalize(requested);
            if (index.Accounts.Any(a => a.Alias == normalized))
            {
                return normalized;
            }

            throw new AccountException(
                $"No account named '{normalized}'. Known accounts: {DescribeAliases(index)}.");
        }

        if (index.Accounts.Count == 0)
        {
            throw new AccountException(
                "No accounts are configured. Use the 'add_account' tool to add one.");
        }

        // A single account is always the default, whatever the index says. This keeps
        // single-account setups behaving exactly as they did before multi-account support.
        if (index.Accounts.Count == 1)
        {
            return index.Accounts[0].Alias;
        }

        if (!string.IsNullOrWhiteSpace(index.DefaultAlias)
            && index.Accounts.Any(a => a.Alias == index.DefaultAlias))
        {
            return index.DefaultAlias;
        }

        throw new AccountException(
            "Several accounts are configured and none is marked default. " +
            $"Pass 'account', or run 'set_default_account'. Known accounts: {DescribeAliases(index)}.");
    }

    private static string DescribeAliases(AccountIndex index) =>
        index.Accounts.Count == 0 ? "(none)" : string.Join(", ", index.Accounts.Select(a => a.Alias));

    private static string RequireValidAlias(string alias)
    {
        if (!AccountAlias.IsValid(alias))
        {
            throw new AccountException($"'{alias}' is not a valid account alias. {AccountAlias.Rule}");
        }

        return AccountAlias.Normalize(alias);
    }

    private static EmailAccount RequireExisting(AccountIndex index, string alias)
    {
        var account = index.Accounts.FirstOrDefault(a => a.Alias == alias);
        if (account is null)
        {
            throw new AccountException(
                $"No account named '{alias}'. Known accounts: {DescribeAliases(index)}.");
        }

        return account;
    }

    private async Task StoreClientCredentialsAsync(
        string alias,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new AccountException("Client ID is required.");
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new AccountException("Client Secret is required.");
        }

        if (!clientId.Contains(".apps.googleusercontent.com", StringComparison.Ordinal))
        {
            throw new AccountException(
                "Client ID doesn't look right. It should end with '.apps.googleusercontent.com'. " +
                "Make sure you're using the OAuth Client ID, not the project ID.");
        }

        var credentials = new
        {
            installed = new
            {
                client_id = clientId.Trim(),
                client_secret = clientSecret.Trim(),
                auth_uri = "https://accounts.google.com/o/oauth2/auth",
                token_uri = "https://oauth2.googleapis.com/token",
                redirect_uris = new[] { "http://localhost" },
            },
        };

        var json = JsonSerializer.Serialize(credentials);
        await this.tokenStore
            .SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials(alias), json, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MoveTokenAsync(string fromKey, string toKey, CancellationToken cancellationToken)
    {
        var value = await this.tokenStore.LoadTokenAsync(fromKey, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return;
        }

        await this.tokenStore.SaveTokenAsync(toKey, value, cancellationToken).ConfigureAwait(false);
        await this.tokenStore.DeleteTokenAsync(fromKey, cancellationToken).ConfigureAwait(false);
    }
}
