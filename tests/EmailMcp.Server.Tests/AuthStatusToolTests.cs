using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class AuthStatusToolTests
{
    private readonly Mock<IEmailAuthenticator> _authMock = new();

    [Fact]
    public async Task AuthStatus_WhenAlreadyAuthenticated_ReturnsAuthenticatedStatus()
    {
        _authMock.Setup(a => a.IsAuthenticatedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        var result = await AuthStatusTool.AuthStatus(_authMock.Object);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Status").GetString().Should().Be("authenticated");
        doc.RootElement.GetProperty("Provider").GetString().Should().Be("Gmail");
    }

    [Fact]
    public async Task AuthStatus_WhenNotAuthenticated_TriggersAuth()
    {
        _authMock.Setup(a => a.IsAuthenticatedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _authMock.Setup(a => a.AuthenticateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        var result = await AuthStatusTool.AuthStatus(_authMock.Object);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Status").GetString().Should().Be("authenticated");
        _authMock.Verify(a => a.AuthenticateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthStatus_WhenAuthFails_ReturnsFailedStatus()
    {
        _authMock.Setup(a => a.IsAuthenticatedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _authMock.Setup(a => a.AuthenticateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        var result = await AuthStatusTool.AuthStatus(_authMock.Object);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Status").GetString().Should().Be("failed");
    }

    [Fact]
    public async Task AuthStatus_ForceReauth_RevokesFirst()
    {
        _authMock.Setup(a => a.IsAuthenticatedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _authMock.Setup(a => a.AuthenticateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _authMock.SetupGet(a => a.ProviderName).Returns("Gmail");

        await AuthStatusTool.AuthStatus(_authMock.Object, forceReauth: true);

        _authMock.Verify(a => a.RevokeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
