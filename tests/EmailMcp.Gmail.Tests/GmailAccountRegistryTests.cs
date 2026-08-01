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
        new GmailOptions(),
        NullLoggerFactory.Instance,
        NullLogger<GmailAccountRegistry>.Instance);

    [Fact]
    public async Task ResolveAlias_WithNoAccounts_ExplainsHowToAddOne()
    {
        var act = () => NewSubject().ResolveAliasAsync(null);

        (await act.Should().ThrowAsync<AccountException>())
            .WithMessage("*add_account*");
    }

    [Fact]
    public async Task ResolveAlias_WithExactlyOneAccount_UsesIt_EvenWithNoDefaultRecorded()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: false);

        var alias = await subject.ResolveAliasAsync(null);

        alias.Should().Be("studio", "a single account is always the default");
    }

    [Fact]
    public async Task ResolveAlias_WithSeveralAccountsAndADefault_UsesTheDefault()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("personal", ClientId, Secret, setDefault: false);
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: true);

        (await subject.ResolveAliasAsync(null)).Should().Be("studio");
    }

    [Fact]
    public async Task ResolveAlias_WithSeveralAccountsAndNoDefault_AsksWhich()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("personal", ClientId, Secret, setDefault: false);
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: false);
        await subject.SetDefaultAccountAsync("personal");

        // Remove the default, leaving two-plus accounts with none marked.
        await subject.AddAccountAsync("work", ClientId, Secret, setDefault: false);
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
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: true);

        var act = () => subject.ResolveAliasAsync("studlo");

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*studio*");
        (await subject.ListAccountsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AddAccount_RejectsInvalidAlias()
    {
        var act = () => NewSubject().AddAccountAsync("Not Valid", ClientId, Secret, setDefault: false);

        await act.Should().ThrowAsync<AccountException>();
    }

    [Fact]
    public async Task AddAccount_RejectsDuplicateAlias()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: false);

        var act = () => subject.AddAccountAsync("studio", ClientId, Secret, setDefault: false);

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*already exists*");
    }

    [Fact]
    public async Task AddAccount_RejectsClientIdThatIsNotAGoogleOAuthClient()
    {
        var act = () => NewSubject().AddAccountAsync("studio", "some-project-id", Secret, setDefault: false);

        (await act.Should().ThrowAsync<AccountException>()).WithMessage("*apps.googleusercontent.com*");
    }

    [Fact]
    public async Task AddAccount_StoresCredentialsUnderTheAccountNamespace()
    {
        await NewSubject().AddAccountAsync("studio", ClientId, Secret, setDefault: false);

        _store.Keys.Should().Contain(AccountKeys.ClientCredentials("studio"));
    }

    [Fact]
    public async Task RenameAccount_MovesStoredSecretsAndKeepsDefault()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: true);
        await _store.SaveTokenAsync(AccountKeys.OAuthToken("studio"), "token-data");

        await subject.RenameAccountAsync("studio", "work");

        _store.Keys.Should().NotContain(AccountKeys.ClientCredentials("studio"));
        _store.Keys.Should().Contain(AccountKeys.ClientCredentials("work"));
        (await _store.LoadTokenAsync(AccountKeys.OAuthToken("work"))).Should().Be("token-data");
        (await subject.ResolveAliasAsync(null)).Should().Be("work");
    }

    [Fact]
    public async Task RemoveAccount_DeletesSecrets_AndPromotesTheSurvivorToDefault()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("personal", ClientId, Secret, setDefault: false);
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: true);

        await subject.RemoveAccountAsync("studio", revokeRemote: false);

        _store.Keys.Should().NotContain(AccountKeys.ClientCredentials("studio"));
        (await subject.ListAccountsAsync()).Should().ContainSingle().Which.Alias.Should().Be("personal");
        (await subject.ResolveAliasAsync(null)).Should().Be("personal");
    }

    [Fact]
    public async Task GetAuthenticator_ReturnsTheSameInstanceForAnAlias()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: true);

        var first = await subject.GetAuthenticatorAsync("studio");
        var second = await subject.GetAuthenticatorAsync("studio");

        second.Should().BeSameAs(first, "the cached credential would otherwise be discarded");
    }

    [Fact]
    public async Task GetAuthenticator_ReturnsDifferentInstancesForDifferentAliases()
    {
        var subject = NewSubject();
        await subject.AddAccountAsync("personal", ClientId, Secret, setDefault: true);
        await subject.AddAccountAsync("studio", ClientId, Secret, setDefault: false);

        var personal = await subject.GetAuthenticatorAsync("personal");
        var studio = await subject.GetAuthenticatorAsync("studio");

        studio.Should().NotBeSameAs(personal);
    }
}
