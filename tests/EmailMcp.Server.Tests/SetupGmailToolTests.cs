using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

/// <summary>
/// setup_gmail now delegates to the account registry rather than writing the token store itself.
/// Client ID and secret validation moved with it, and is covered by the registry's own tests.
/// </summary>
public class SetupGmailToolTests
{
    private const string ValidClientId = "123456789-abc.apps.googleusercontent.com";
    private const string ValidSecret = "GOCSPX-secret123";

    private readonly Mock<IAccountRegistry> _registryMock = new();

    [Fact]
    public async Task SetupGmail_WithNoAccounts_CreatesTheDefaultAccount()
    {
        WithAccounts();

        var result = await SetupGmailTool.SetupGmail(_registryMock.Object, ValidClientId, ValidSecret);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("Account").GetString().Should().Be("default");

        _registryMock.Verify(
            r => r.AddAccountAsync("default", ValidClientId, ValidSecret, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetupGmail_WithOneAccount_ReplacesItsCredentials()
    {
        WithAccounts("studio");

        var result = await SetupGmailTool.SetupGmail(_registryMock.Object, ValidClientId, ValidSecret);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("Account").GetString().Should().Be("studio");

        _registryMock.Verify(
            r => r.UpdateCredentialsAsync("studio", ValidClientId, ValidSecret, It.IsAny<CancellationToken>()),
            Times.Once);
        _registryMock.Verify(
            r => r.AddAccountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetupGmail_WithSeveralAccounts_RefusesAndNamesThem()
    {
        WithAccounts("personal", "studio");

        var result = await SetupGmailTool.SetupGmail(_registryMock.Object, ValidClientId, ValidSecret);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        var error = doc.RootElement.GetProperty("Error").GetString();
        error.Should().Contain("personal").And.Contain("studio");
        error.Should().Contain("add_account");
    }

    [Fact]
    public async Task SetupGmail_WhenRegistryRejectsCredentials_ReturnsTheReason()
    {
        WithAccounts();
        _registryMock
            .Setup(r => r.AddAccountAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountException("Client ID doesn't look right."));

        var result = await SetupGmailTool.SetupGmail(_registryMock.Object, "not-a-client-id", ValidSecret);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Error").GetString().Should().Contain("Client ID");
    }

    private void WithAccounts(params string[] aliases)
    {
        var accounts = aliases
            .Select(alias => new EmailAccount(alias, EmailAddress: null, DateTimeOffset.UtcNow))
            .ToList();

        _registryMock
            .Setup(r => r.ListAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
    }
}
