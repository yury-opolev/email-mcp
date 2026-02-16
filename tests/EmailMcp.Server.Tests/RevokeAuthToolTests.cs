using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class RevokeAuthToolTests
{
    private readonly Mock<IEmailAuthenticator> _authMock = new();

    [Fact]
    public async Task RevokeAuth_CallsRevokeAsync()
    {
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        await RevokeAuthTool.RevokeAuth(_authMock.Object);

        _authMock.Verify(a => a.RevokeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAuth_ReturnsSuccessWithProvider()
    {
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        var result = await RevokeAuthTool.RevokeAuth(_authMock.Object);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("Provider").GetString().Should().Be("Gmail");
    }

    [Fact]
    public async Task RevokeAuth_MessageGuidesToReauthenticate()
    {
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        var result = await RevokeAuthTool.RevokeAuth(_authMock.Object);
        var doc = JsonDocument.Parse(result);

        var message = doc.RootElement.GetProperty("Message").GetString();
        message.Should().Contain("auth_status");
    }
}
