using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EmailMcp.Abstractions;

/// <summary>
/// Reads and writes the account index through <see cref="ITokenStore"/>, and migrates the
/// pre-multi-account keys on first use.
/// </summary>
public sealed class AccountIndexStore
{
    private static readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = false };

    private readonly ITokenStore tokenStore;
    private readonly ILogger<AccountIndexStore> logger;

    public AccountIndexStore(ITokenStore tokenStore, ILogger<AccountIndexStore> logger)
    {
        this.tokenStore = tokenStore;
        this.logger = logger;
    }

    /// <summary>
    /// Loads the index, running the legacy migration first if it has not run yet.
    /// Returns an empty index on a fresh install.
    /// </summary>
    public async Task<AccountIndex> LoadAsync(CancellationToken cancellationToken = default)
    {
        await this.MigrateLegacyIfNeededAsync(cancellationToken).ConfigureAwait(false);

        var json = await this.tokenStore.LoadTokenAsync(AccountKeys.Index, cancellationToken).ConfigureAwait(false);
        var index = json is null ? new AccountIndex() : Deserialize(json, this.logger);

        await this.MigrateCredentialsToSharedIfNeededAsync(index, cancellationToken).ConfigureAwait(false);

        return index;
    }

    private static AccountIndex Deserialize(string json, ILogger<AccountIndexStore> logger)
    {
        try
        {
            return JsonSerializer.Deserialize<AccountIndex>(json, serializerOptions) ?? new AccountIndex();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Account index is corrupt; treating it as empty");
            return new AccountIndex();
        }
    }

    /// <summary>Persists the index.</summary>
    public async Task SaveAsync(AccountIndex index, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(index, serializerOptions);
        await this.tokenStore.SaveTokenAsync(AccountKeys.Index, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the pre-multi-account keys under the "default" alias, once.
    /// </summary>
    /// <remarks>
    /// Idempotent: the index is written last, so a crash part-way leaves no index and the next
    /// run simply repeats the copy, which overwrites rather than duplicating.
    /// </remarks>
    private async Task MigrateLegacyIfNeededAsync(CancellationToken cancellationToken)
    {
        if (await this.tokenStore.ExistsAsync(AccountKeys.Index, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var legacyCredentials = await this.tokenStore
            .LoadTokenAsync(AccountKeys.LegacyClientCredentials, cancellationToken)
            .ConfigureAwait(false);

        if (legacyCredentials is null)
        {
            return;
        }

        this.logger.LogInformation(
            "Migrating single-account configuration to the '{Alias}' account",
            AccountKeys.LegacyAlias);

        await this.tokenStore.SaveTokenAsync(
            AccountKeys.SharedClientCredentials,
            legacyCredentials,
            cancellationToken).ConfigureAwait(false);

        var legacyToken = await this.tokenStore
            .LoadTokenAsync(AccountKeys.LegacyOAuthToken, cancellationToken)
            .ConfigureAwait(false);

        if (legacyToken is not null)
        {
            await this.tokenStore.SaveTokenAsync(
                AccountKeys.OAuthToken(AccountKeys.LegacyAlias),
                legacyToken,
                cancellationToken).ConfigureAwait(false);
        }

        var index = new AccountIndex
        {
            DefaultAlias = AccountKeys.LegacyAlias,
            Accounts =
            [
                new EmailAccount(AccountKeys.LegacyAlias, EmailAddress: null, DateTimeOffset.UtcNow),
            ],
        };

        await this.SaveAsync(index, cancellationToken).ConfigureAwait(false);

        await this.tokenStore.DeleteTokenAsync(AccountKeys.LegacyClientCredentials, cancellationToken)
            .ConfigureAwait(false);
        await this.tokenStore.DeleteTokenAsync(AccountKeys.LegacyOAuthToken, cancellationToken)
            .ConfigureAwait(false);

        this.logger.LogInformation("Migration complete; the previous session remains authenticated");
    }

    /// <summary>
    /// Promotes per-account client credentials to the single shared key, once.
    /// </summary>
    /// <remarks>
    /// The shared copy is written before any per-account copy is deleted, so a crash part-way
    /// leaves the credentials readable and the next run simply repeats the promotion.
    /// </remarks>
    private async Task MigrateCredentialsToSharedIfNeededAsync(
        AccountIndex index,
        CancellationToken cancellationToken)
    {
        if (index.Accounts.Count == 0)
        {
            return;
        }

        var hasShared = await this.tokenStore
            .ExistsAsync(AccountKeys.SharedClientCredentials, cancellationToken)
            .ConfigureAwait(false);

        if (!hasShared)
        {
            // The default account's copy wins. Falling back to index order keeps the rule total
            // for an index that records no default, which no supported flow produces.
            var preferred = index.Accounts.FirstOrDefault(a => a.Alias == index.DefaultAlias)
                ?? index.Accounts[0];

            var promoted = await this.tokenStore
                .LoadTokenAsync(
                    AccountKeys.LegacyAccountClientCredentials(preferred.Alias),
                    cancellationToken)
                .ConfigureAwait(false);

            promoted ??= await this.FindAnyAccountCredentialsAsync(index, cancellationToken)
                .ConfigureAwait(false);

            if (promoted is null)
            {
                return;
            }

            this.logger.LogInformation(
                "Promoting the client credentials of account '{Alias}' to the shared key",
                preferred.Alias);

            await this.tokenStore
                .SaveTokenAsync(AccountKeys.SharedClientCredentials, promoted, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var account in index.Accounts)
        {
            await this.tokenStore
                .DeleteTokenAsync(
                    AccountKeys.LegacyAccountClientCredentials(account.Alias),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string?> FindAnyAccountCredentialsAsync(
        AccountIndex index,
        CancellationToken cancellationToken)
    {
        foreach (var account in index.Accounts)
        {
            var value = await this.tokenStore
                .LoadTokenAsync(
                    AccountKeys.LegacyAccountClientCredentials(account.Alias),
                    cancellationToken)
                .ConfigureAwait(false);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
