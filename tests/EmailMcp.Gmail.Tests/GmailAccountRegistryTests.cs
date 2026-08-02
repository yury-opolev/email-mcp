using EmailMcp.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmailMcp.Gmail.Tests;

public class GmailAccountRegistryTests
{
    private const string ClientId = "123456789-abc.apps.googleusercontent.com";
    private const string Secret = "GOCSPX-secret";

    private readonly InMemoryTokenStore _store = new();

    private GmailAccountRegistry NewSubject() => new(
        _store,
        new AccountIndexStore(_store, NullLogger<AccountIndexStore>.Instance),
        new GmailOptions { CredentialsPath = Path.Combine(Path.GetTempPath(), "email-mcp-tests-no-such-credentials.json") },
        NullLoggerFactory.Instance,
        NullLogger<GmailAccountRegistry>.Instance);

    [Fact]
    public async Task ResolveAlias_WithNoAccounts_ExplainsHowToGetStarted()
    {
        var act = () => NewSubject().ResolveAliasAsync(null);

        (await act.Should().ThrowAsync<AccountException>())
            .WithMessage("*setup_gmail*");
    }

    [Fact]
    public async Task ResolveAlias_WithExactlyOneAccount_UsesIt_EvenWithNoDefaultRecorded()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: false);

        var alias = await subject.ResolveAliasAsync(null);

        alias.Should().Be("studio", "a single account is always the default");
    }

    [Fact]
    public async Task ResolveAlias_WithSeveralAccountsAndADefault_UsesTheDefault()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: false);
        await subject.AddAccountAsync("studio", setDefault: true);

        (await subject.ResolveAliasAsync(null)).Should().Be("studio");
    }

    [Fact]
    public async Task ResolveAlias_WithSeveralAccountsAndNoDefault_AsksWhich()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: false);
        await subject.AddAccountAsync("studio", setDefault: false);
        await subject.SetDefaultAccountAsync("personal");

        // Remove the default, leaving two-plus accounts with none marked.
        await subject.AddAccountAsync("work", setDefault: false);
        var index = await new AccountIndexStore(_store, NullLogger<AccountIndexStore>.Instance).LoadAsync();
        index.DefaultAlias = null;
        await new AccountIndexStore(_store, NullLogger<AccountIndexStore>.Instance).SaveAsync(index);

        var act = () => NewSubject().ResolveAliasAsync(null);

        (await act.Should().ThrowAsync<AccountException>())
            .WithMessage("*set_default_account*");
    }

    [Fact]
    public async Task ResolveAlias_WithUnknownAlias_ListsKnownOnes_AndDoesNotCreateIt()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: true);

        var act = () => subject.ResolveAliasAsync("studlo");

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*studio*");
        (await subject.ListAccountsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AddAccount_RejectsInvalidAlias()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);

        var act = () => subject.AddAccountAsync("Not Valid", setDefault: false);

        await act.Should().ThrowAsync<AccountException>();
    }

    [Fact]
    public async Task AddAccount_RejectsDuplicateAlias()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: false);

        var act = () => subject.AddAccountAsync("studio", setDefault: false);

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*already exists*");
    }

    [Fact]
    public async Task AddAccount_WithNoSharedCredentials_PointsAtSetupGmail()
    {
        var act = () => NewSubject().AddAccountAsync("studio", setDefault: false);

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*setup_gmail*");
    }

    [Fact]
    public async Task AddAccount_WithACredentialsFileButNoStoredPair_IsAllowed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"email-mcp-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{}");
        try
        {
            var subject = new GmailAccountRegistry(
                _store,
                new AccountIndexStore(_store, NullLogger<AccountIndexStore>.Instance),
                new GmailOptions { CredentialsPath = path },
                NullLoggerFactory.Instance,
                NullLogger<GmailAccountRegistry>.Instance);

            var act = () => subject.AddAccountAsync("studio", setDefault: false);

            await act.Should().NotThrowAsync();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SetSharedCredentials_StoresOneCopyUnderTheSharedKey()
    {
        var subject = NewSubject();

        await subject.SetSharedCredentialsAsync(ClientId, Secret);

        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
        _store.Keys.Should().NotContain(k => k.EndsWith("--client-credentials", StringComparison.Ordinal)
            && k != AccountKeys.SharedClientCredentials);
    }

    [Fact]
    public async Task SetSharedCredentials_WithTheSameClientId_ReportsNoChange()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);

        var result = await subject.SetSharedCredentialsAsync(ClientId, "GOCSPX-rotated-secret");

        result.ClientIdChanged.Should().BeFalse();
    }

    [Fact]
    public async Task SetSharedCredentials_WithADifferentClientId_ListsAccountsNeedingReauth()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: true);
        await subject.AddAccountAsync("studio", setDefault: false);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        var result = await subject.SetSharedCredentialsAsync(
            "999-xyz.apps.googleusercontent.com",
            Secret);

        result.ClientIdChanged.Should().BeTrue();
        result.AuthenticatedAccounts.Should().BeEquivalentTo(["studio"]);
    }

    [Fact]
    public async Task SetSharedCredentials_RejectsAClientIdThatIsNotAnOAuthClientId()
    {
        var act = () => NewSubject().SetSharedCredentialsAsync("my-project-id", Secret);

        (await act.Should().ThrowAsync<AccountException>())
            .WithMessage("*apps.googleusercontent.com*");
    }

    [Fact]
    public async Task RenameAccount_MovesTheTokenButNotTheCredentials()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: true);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        await subject.RenameAccountAsync("studio", "work");

        (await _store.LoadTokenAsync(AccountKeys.OAuthToken("work"))).Should().Be("TOKEN");
        (await _store.ExistsAsync(AccountKeys.OAuthToken("studio"))).Should().BeFalse();
        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
    }

    [Fact]
    public async Task RenameAccount_KeepsTheDefaultUnderTheNewAlias()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: true);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "token-data");

        await subject.RenameAccountAsync("studio", "work");

        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
        (await _store.LoadTokenAsync(AccountKeys.OAuthToken("work"))).Should().Be("token-data");
        (await subject.ResolveAliasAsync(null)).Should().Be("work");
    }

    [Fact]
    public async Task RemoveAccount_PromotesTheSurvivorToDefault()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: false);
        await subject.AddAccountAsync("studio", setDefault: true);

        await subject.RemoveAccountAsync("studio", revokeRemote: false);

        (await subject.ListAccountsAsync()).Should().ContainSingle().Which.Alias.Should().Be("personal");
        (await subject.ResolveAliasAsync(null)).Should().Be("personal");
    }

    [Fact]
    public async Task RemoveAccount_DeletesTheAccountsOAuthToken()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: true);
        await subject.AddAccountAsync("studio", setDefault: false);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "TOKEN");

        await subject.RemoveAccountAsync("studio", revokeRemote: false);

        (await _store.ExistsAsync(AccountKeys.OAuthToken("studio"))).Should().BeFalse();
        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
    }

    [Fact]
    public async Task RemoveAccount_LeavesTheSharedCredentialsIntact()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: true);
        await subject.AddAccountAsync("studio", setDefault: false);

        await subject.RemoveAccountAsync("studio", revokeRemote: false);

        _store.Keys.Should().Contain(AccountKeys.SharedClientCredentials);
        (await subject.ResolveAliasAsync(null)).Should().Be("personal");
    }

    [Fact]
    public async Task GetAuthenticator_ReturnsTheSameInstanceForAnAlias()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("studio", setDefault: true);

        var first = await subject.GetAuthenticatorAsync("studio");
        var second = await subject.GetAuthenticatorAsync("studio");

        second.Should().BeSameAs(first, "the cached credential would otherwise be discarded");
    }

    [Fact]
    public async Task GetAuthenticator_ReturnsDifferentInstancesForDifferentAliases()
    {
        var subject = NewSubject();
        await subject.SetSharedCredentialsAsync(ClientId, Secret);
        await subject.AddAccountAsync("personal", setDefault: true);
        await subject.AddAccountAsync("studio", setDefault: false);

        var personal = await subject.GetAuthenticatorAsync("personal");
        var studio = await subject.GetAuthenticatorAsync("studio");

        studio.Should().NotBeSameAs(personal);
    }
}
