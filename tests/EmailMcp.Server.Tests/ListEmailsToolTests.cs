using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class ListEmailsToolTests
{
    private readonly Mock<IEmailProvider> _providerMock = new();

    [Fact]
    public async Task ListEmails_ReturnsJsonWithEmailSummaries()
    {
        var emails = new List<EmailMessage>
        {
            new()
            {
                Id = "msg-1",
                ThreadId = "thread-1",
                Subject = "Meeting Tomorrow",
                From = new EmailAddress("alice@test.com", "Alice"),
                Date = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero),
                Snippet = "Don't forget...",
                IsUnread = true,
                LabelIds = ["INBOX", "UNREAD"],
            },
        };

        _providerMock.Setup(p => p.ListEmailsAsync(20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emails);

        var result = await ListEmailsTool.ListEmails(_providerMock.Object);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("Id").GetString().Should().Be("msg-1");
        doc.RootElement[0].GetProperty("Subject").GetString().Should().Be("Meeting Tomorrow");
    }

    [Fact]
    public async Task ListEmails_ClampsMaxResults()
    {
        _providerMock.Setup(p => p.ListEmailsAsync(50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailMessage>());

        await ListEmailsTool.ListEmails(_providerMock.Object, maxResults: 100);

        _providerMock.Verify(p => p.ListEmailsAsync(50, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListEmails_PassesLabelId()
    {
        _providerMock.Setup(p => p.ListEmailsAsync(20, "SENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailMessage>());

        await ListEmailsTool.ListEmails(_providerMock.Object, labelId: "SENT");

        _providerMock.Verify(p => p.ListEmailsAsync(20, "SENT", It.IsAny<CancellationToken>()), Times.Once);
    }
}
