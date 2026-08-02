using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class SetupGmailToolTests
{
    private const string ClientId = "123456789-abc.apps.googleusercontent.com";
    private const string Secret = "GOCSPX-secret";

    private static Mock<IAccountRegistry> NewRegistry(
        IReadOnlyList<EmailAccount> accounts,
        CredentialUpdateResult result)
    {
        var registry = new Mock<IAccountRegistry>();
        registry
            .Setup(r => r.ListAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        registry
            .Setup(r => r.SetSharedCredentialsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        registry
            .Setup(r => r.AddAccountAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return registry;
    }

    [Fact]
    public async Task WithNoAccounts_StoresCredentialsAndCreatesDefault()
    {
        var registry = NewRegistry([], new CredentialUpdateResult(false, []));

        var response = await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        registry.Verify(r => r.AddAccountAsync(
            AccountKeys.LegacyAlias, true, It.IsAny<CancellationToken>()), Times.Once);
        response.Should().Contain("auth_status");
    }

    [Fact]
    public async Task WithAccountsAlready_DoesNotCreateAnother()
    {
        var accounts = new[] { new EmailAccount("studio", null, DateTimeOffset.UtcNow) };
        var registry = NewRegistry(accounts, new CredentialUpdateResult(false, []));

        await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        registry.Verify(r => r.AddAccountAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithSeveralAccounts_IsAllowedBecauseRotationIsLegitimate()
    {
        var accounts = new[]
        {
            new EmailAccount("personal", null, DateTimeOffset.UtcNow),
            new EmailAccount("studio", null, DateTimeOffset.UtcNow),
        };
        var registry = NewRegistry(accounts, new CredentialUpdateResult(false, []));

        var response = await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);
        var doc = JsonDocument.Parse(response);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WhenTheClientIdChanged_NamesTheAccountsNeedingReauth()
    {
        var accounts = new[] { new EmailAccount("studio", null, DateTimeOffset.UtcNow) };
        var registry = NewRegistry(accounts, new CredentialUpdateResult(true, ["studio"]));

        var response = await SetupGmailTool.SetupGmail(registry.Object, ClientId, Secret);

        response.Should().Contain("studio").And.Contain("auth_status");
    }

    [Fact]
    public async Task WhenTheRegistryRejectsTheCredentials_ReturnsTheError()
    {
        var registry = new Mock<IAccountRegistry>();
        registry
            .Setup(r => r.ListAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        registry
            .Setup(r => r.SetSharedCredentialsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountException("Client ID doesn't look right."));

        var response = await SetupGmailTool.SetupGmail(registry.Object, "nope", Secret);
        var doc = JsonDocument.Parse(response);

        doc.RootElement.GetProperty("Error").GetString().Should().Contain("doesn't look right");
    }
}
