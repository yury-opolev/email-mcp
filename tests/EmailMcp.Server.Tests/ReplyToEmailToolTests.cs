using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class ReplyToEmailToolTests
{
    private readonly Mock<IEmailProvider> providerMock = new();

    [Fact]
    public async Task ReplyToEmail_PassesMessageIdThroughAndReturnsSentId()
    {
        ReplyToEmailRequest? captured = null;
        this.providerMock
            .Setup(p => p.ReplyToEmailAsync(It.IsAny<ReplyToEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ReplyToEmailRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync("sent-123");

        var result = await ReplyToEmailTool.ReplyToEmail(
            TestRegistry.For(this.providerMock.Object),
            messageId: "orig-abc",
            body: "Merci beaucoup d'avoir pris le temps.");

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("MessageId").GetString().Should().Be("sent-123");
        doc.RootElement.GetProperty("InReplyTo").GetString().Should().Be("orig-abc");

        captured.Should().NotBeNull();
        captured!.MessageId.Should().Be("orig-abc");
        captured.Body.Should().Be("Merci beaucoup d'avoir pris le temps.");

        // A reply must never go out as a fresh message - that is the entire point of the tool.
        this.providerMock.Verify(
            p => p.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReplyToEmail_DefaultsToSenderOnly()
    {
        ReplyToEmailRequest? captured = null;
        this.providerMock
            .Setup(p => p.ReplyToEmailAsync(It.IsAny<ReplyToEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ReplyToEmailRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync("sent-1");

        await ReplyToEmailTool.ReplyToEmail(
            TestRegistry.For(this.providerMock.Object), messageId: "orig", body: "b");

        // Reply-all is the more damaging mistake, so it must be opt-in.
        captured!.ReplyAll.Should().BeFalse();
        captured.Cc.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplyToEmail_HonoursReplyAllAndExtraCc()
    {
        ReplyToEmailRequest? captured = null;
        this.providerMock
            .Setup(p => p.ReplyToEmailAsync(It.IsAny<ReplyToEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ReplyToEmailRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync("sent-1");

        await ReplyToEmailTool.ReplyToEmail(
            TestRegistry.For(this.providerMock.Object),
            messageId: "orig",
            body: "b",
            replyAll: true,
            cc: "extra@example.com, Bob <bob@example.com>");

        captured!.ReplyAll.Should().BeTrue();
        captured.Cc.Select(a => a.Address).Should().Equal("extra@example.com", "bob@example.com");
    }

    [Theory]
    [InlineData("", "body", "'messageId' field is required")]
    [InlineData("orig", "", "'body' or 'bodyHtml'")]
    public async Task ReplyToEmail_RejectsMissingRequiredFields(
        string messageId, string body, string expected)
    {
        var result = await ReplyToEmailTool.ReplyToEmail(
            TestRegistry.For(this.providerMock.Object), messageId, body);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Error").GetString().Should().Contain(expected);

        this.providerMock.Verify(
            p => p.ReplyToEmailAsync(It.IsAny<ReplyToEmailRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReplyToEmail_SurfacesProviderFailure()
    {
        this.providerMock
            .Setup(p => p.ReplyToEmailAsync(It.IsAny<ReplyToEmailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mailbox offline"));

        var result = await ReplyToEmailTool.ReplyToEmail(
            TestRegistry.For(this.providerMock.Object), messageId: "orig", body: "b");

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Error").GetString().Should().Contain("mailbox offline");
    }
}
