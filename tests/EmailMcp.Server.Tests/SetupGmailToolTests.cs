using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class SetupGmailToolTests
{
    private readonly Mock<ITokenStore> _tokenStoreMock = new();

    [Fact]
    public async Task SetupGmail_ValidCredentials_SavesEncrypted()
    {
        var clientId = "123456789-abc.apps.googleusercontent.com";
        var clientSecret = "GOCSPX-secret123";

        var result = await SetupGmailTool.SetupGmail(_tokenStoreMock.Object, clientId, clientSecret);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();

        _tokenStoreMock.Verify(s => s.SaveTokenAsync(
            "gmail-client-credentials",
            It.Is<string>(json => json.Contains(clientId) && json.Contains(clientSecret)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetupGmail_EmptyClientId_ReturnsError()
    {
        var result = await SetupGmailTool.SetupGmail(_tokenStoreMock.Object, "", "secret");
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Message").GetString().Should().Contain("Client ID");
    }

    [Fact]
    public async Task SetupGmail_EmptyClientSecret_ReturnsError()
    {
        var result = await SetupGmailTool.SetupGmail(
            _tokenStoreMock.Object,
            "123-abc.apps.googleusercontent.com",
            "");
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Message").GetString().Should().Contain("Client Secret");
    }

    [Fact]
    public async Task SetupGmail_InvalidClientIdFormat_ReturnsError()
    {
        var result = await SetupGmailTool.SetupGmail(_tokenStoreMock.Object, "not-a-valid-id", "secret");
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Message").GetString().Should().Contain("doesn't look right");
    }

    [Fact]
    public async Task SetupGmail_SavedJson_HasCorrectStructure()
    {
        string? savedJson = null;
        _tokenStoreMock.Setup(s => s.SaveTokenAsync("gmail-client-credentials", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) => savedJson = json)
            .Returns(Task.CompletedTask);

        await SetupGmailTool.SetupGmail(
            _tokenStoreMock.Object,
            "123-abc.apps.googleusercontent.com",
            "my-secret");

        savedJson.Should().NotBeNull();
        var doc = JsonDocument.Parse(savedJson!);
        doc.RootElement.GetProperty("installed").GetProperty("client_id").GetString()
            .Should().Be("123-abc.apps.googleusercontent.com");
        doc.RootElement.GetProperty("installed").GetProperty("client_secret").GetString()
            .Should().Be("my-secret");
        doc.RootElement.GetProperty("installed").GetProperty("auth_uri").GetString()
            .Should().Contain("google");
    }
}
