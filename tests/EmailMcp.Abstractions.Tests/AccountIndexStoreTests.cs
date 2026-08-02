using EmailMcp.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmailMcp.Abstractions.Tests;

public class AccountIndexStoreTests
{
    private readonly InMemoryTokenStore _store = new();

    private AccountIndexStore NewSubject() =>
        new(_store, NullLogger<AccountIndexStore>.Instance);

    [Fact]
    public async Task Load_OnFreshInstall_ReturnsEmptyIndex()
    {
        var index = await NewSubject().LoadAsync();

        index.Accounts.Should().BeEmpty();
        index.DefaultAlias.Should().BeNull();
        _store.Keys.Should().BeEmpty("a fresh install must not write anything on read");
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var subject = NewSubject();
        var created = DateTimeOffset.UtcNow;

        await subject.SaveAsync(new AccountIndex
        {
            DefaultAlias = "studio",
            Accounts =
            [
                new EmailAccount("studio", "yury@spacekubegames.com", created),
                new EmailAccount("personal", null, created),
            ],
        });

        var loaded = await NewSubject().LoadAsync();

        loaded.DefaultAlias.Should().Be("studio");
        loaded.Accounts.Should().HaveCount(2);
        loaded.Accounts[0].Alias.Should().Be("studio");
        loaded.Accounts[0].EmailAddress.Should().Be("yury@spacekubegames.com");
        loaded.Accounts[1].EmailAddress.Should().BeNull();
    }

    [Fact]
    public async Task Load_MigratesLegacyKeys_IntoTheDefaultAccount()
    {
        await _store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "{\"installed\":{}}");
        await _store.SaveTokenAsync(AccountKeys.LegacyOAuthToken, "{\"access_token\":\"abc\"}");

        var index = await NewSubject().LoadAsync();

        index.Accounts.Should().ContainSingle().Which.Alias.Should().Be(AccountKeys.LegacyAlias);
        index.DefaultAlias.Should().Be(AccountKeys.LegacyAlias);

        (await _store.LoadTokenAsync(AccountKeys.SharedClientCredentials))
            .Should().Be("{\"installed\":{}}");
        (await _store.LoadTokenAsync(AccountKeys.OAuthToken(AccountKeys.LegacyAlias)))
            .Should().Be("{\"access_token\":\"abc\"}", "the existing session must survive the upgrade");

        _store.Keys.Should().NotContain(AccountKeys.LegacyClientCredentials);
        _store.Keys.Should().NotContain(AccountKeys.LegacyOAuthToken);
    }

    [Fact]
    public async Task Load_MigratesCredentials_EvenWhenNeverAuthenticated()
    {
        await _store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "{\"installed\":{}}");

        var index = await NewSubject().LoadAsync();

        index.Accounts.Should().ContainSingle();
        (await _store.ExistsAsync(AccountKeys.OAuthToken(AccountKeys.LegacyAlias))).Should().BeFalse();
    }

    [Fact]
    public async Task Load_IsIdempotent_AndDoesNotResurrectMigration()
    {
        await _store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "{\"installed\":{}}");

        await NewSubject().LoadAsync();

        // A later account added after migration must not be wiped by a second run.
        var index = await NewSubject().LoadAsync();
        index.Accounts.Add(new EmailAccount("studio", null, DateTimeOffset.UtcNow));
        await NewSubject().SaveAsync(index);

        var reloaded = await NewSubject().LoadAsync();

        reloaded.Accounts.Should().HaveCount(2);
        reloaded.Accounts.Select(a => a.Alias).Should().Contain(["default", "studio"]);
    }

    [Fact]
    public async Task Load_WhenIndexIsCorrupt_TreatsItAsEmptyRatherThanThrowing()
    {
        await _store.SaveTokenAsync(AccountKeys.Index, "this is not json");

        var index = await NewSubject().LoadAsync();

        index.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_PromotesPerAccountCredentialsToTheSharedKey()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.Index, """
            {"DefaultAlias":"studio","Accounts":[
              {"Alias":"personal","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"},
              {"Alias":"studio","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"}]}
            """);
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("personal"), "PERSONAL");
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("studio"), "STUDIO");
        await store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials))
            .Should().Be("STUDIO", "the default account's copy wins");
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials("personal")))
            .Should().BeFalse();
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials("studio")))
            .Should().BeFalse();
        (await store.LoadTokenAsync(AccountKeys.OAuthToken("studio")))
            .Should().Be("TOKEN", "OAuth tokens are never touched by credential migration");
    }

    [Fact]
    public async Task Load_WithNoDefaultRecorded_PromotesTheFirstAccountsCredentials()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.Index, """
            {"DefaultAlias":null,"Accounts":[
              {"Alias":"personal","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"},
              {"Alias":"studio","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"}]}
            """);
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("studio"), "STUDIO");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("STUDIO");
    }

    [Fact]
    public async Task Load_MigratesPreMultiAccountCredentialsStraightToTheSharedKey()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "LEGACY");
        await store.SaveTokenAsync(AccountKeys.LegacyOAuthToken, "TOKEN");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        var index = await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("LEGACY");
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials(AccountKeys.LegacyAlias)))
            .Should().BeFalse("the per-account key is skipped entirely now");
        (await store.LoadTokenAsync(AccountKeys.OAuthToken(AccountKeys.LegacyAlias)))
            .Should().Be("TOKEN");
        index.Accounts.Should().ContainSingle(a => a.Alias == AccountKeys.LegacyAlias);
    }

    [Fact]
    public async Task Load_IsIdempotent()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.LegacyClientCredentials, "LEGACY");
        await store.SaveTokenAsync(AccountKeys.LegacyOAuthToken, "TOKEN");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("LEGACY");
        (await store.LoadTokenAsync(AccountKeys.OAuthToken(AccountKeys.LegacyAlias))).Should().Be("TOKEN");
    }

    [Fact]
    public async Task Load_WithSharedCredentialsAlreadyPresent_LeavesThemAlone()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(AccountKeys.SharedClientCredentials, "SHARED");
        await store.SaveTokenAsync(AccountKeys.Index, """
            {"DefaultAlias":"studio","Accounts":[
              {"Alias":"studio","EmailAddress":null,"CreatedUtc":"2026-01-01T00:00:00+00:00"}]}
            """);
        await store.SaveTokenAsync(AccountKeys.LegacyAccountClientCredentials("studio"), "STALE");

        var subject = new AccountIndexStore(store, NullLogger<AccountIndexStore>.Instance);
        await subject.LoadAsync();

        (await store.LoadTokenAsync(AccountKeys.SharedClientCredentials)).Should().Be("SHARED");
        (await store.ExistsAsync(AccountKeys.LegacyAccountClientCredentials("studio")))
            .Should().BeFalse("stale per-account copies are cleaned up even when shared already exists");
    }
}
