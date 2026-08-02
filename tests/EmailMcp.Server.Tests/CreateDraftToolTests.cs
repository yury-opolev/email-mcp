using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class CreateDraftToolTests
{
    private readonly Mock<IEmailProvider> providerMock = new();

    [Fact]
    public async Task CreateDraft_ReturnsDraftIdAndDoesNotSend()
    {
        SendEmailRequest? captured = null;
        this.providerMock
            .Setup(p => p.CreateDraftAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendEmailRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync("draft-123");

        var result = await CreateDraftTool.CreateDraft(
            TestRegistry.For(this.providerMock.Object),
            to: "Maestra Ilaria <info@maestrailaria.it>",
            subject: "Un'app per l'orologio",
            body: "Buongiorno Ilaria,");

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("DraftId").GetString().Should().Be("draft-123");

        captured.Should().NotBeNull();
        captured!.To.Should().ContainSingle();
        captured.To[0].Address.Should().Be("info@maestrailaria.it");
        captured.To[0].DisplayName.Should().Be("Maestra Ilaria");
        captured.Subject.Should().Be("Un'app per l'orologio");

        // The whole point of a draft: nothing leaves the mailbox.
        this.providerMock.Verify(
            p => p.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateDraft_ParsesCommaSeparatedRecipients()
    {
        SendEmailRequest? captured = null;
        this.providerMock
            .Setup(p => p.CreateDraftAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SendEmailRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync("draft-1");

        await CreateDraftTool.CreateDraft(
            TestRegistry.For(this.providerMock.Object),
            to: "a@example.com, B <b@example.com>",
            subject: "s",
            body: "b",
            cc: "c@example.com");

        captured!.To.Select(a => a.Address).Should().Equal("a@example.com", "b@example.com");
        captured.Cc.Should().ContainSingle().Which.Address.Should().Be("c@example.com");
    }

    [Theory]
    [InlineData("", "subject", "body", "'to' field is required")]
    [InlineData("a@example.com", "", "body", "'subject' field is required")]
    [InlineData("a@example.com", "subject", "", "'body' or 'bodyHtml'")]
    public async Task CreateDraft_RejectsMissingRequiredFields(
        string to, string subject, string body, string expected)
    {
        var result = await CreateDraftTool.CreateDraft(
            TestRegistry.For(this.providerMock.Object), to, subject, body);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Error").GetString().Should().Contain(expected);

        this.providerMock.Verify(
            p => p.CreateDraftAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateDraft_ReportsUnparseableRecipients()
    {
        var result = await CreateDraftTool.CreateDraft(
            TestRegistry.For(this.providerMock.Object), to: ",", subject: "s", body: "b");

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Error").GetString().Should().Contain("valid recipient");
    }

    [Fact]
    public async Task CreateDraft_SurfacesProviderFailure()
    {
        this.providerMock
            .Setup(p => p.CreateDraftAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mailbox offline"));

        var result = await CreateDraftTool.CreateDraft(
            TestRegistry.For(this.providerMock.Object), to: "a@example.com", subject: "s", body: "b");

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("Error").GetString().Should().Contain("mailbox offline");
    }
}
