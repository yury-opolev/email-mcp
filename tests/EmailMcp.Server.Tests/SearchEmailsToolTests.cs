using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class SearchEmailsToolTests
{
    private readonly Mock<IEmailProvider> _providerMock = new();

    [Fact]
    public async Task SearchEmails_PassesQueryToProvider()
    {
        _providerMock.Setup(p => p.SearchEmailsAsync(It.IsAny<EmailSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailMessage>());

        await SearchEmailsTool.SearchEmails(_providerMock.Object, query: "from:alice is:unread");

        _providerMock.Verify(p => p.SearchEmailsAsync(
            It.Is<EmailSearchQuery>(q => q.Query == "from:alice is:unread"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchEmails_PassesStructuredFields()
    {
        _providerMock.Setup(p => p.SearchEmailsAsync(It.IsAny<EmailSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmailMessage>());

        await SearchEmailsTool.SearchEmails(
            _providerMock.Object,
            from: "alice@test.com",
            subject: "report",
            maxResults: 5);

        _providerMock.Verify(p => p.SearchEmailsAsync(
            It.Is<EmailSearchQuery>(q =>
                q.From == "alice@test.com" &&
                q.Subject == "report" &&
                q.MaxResults == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchEmails_ReturnsResultsAsJson()
    {
        var emails = new List<EmailMessage>
        {
            new()
            {
                Id = "search-1",
                ThreadId = "thread-1",
                Subject = "Found it",
                From = new EmailAddress("bob@test.com"),
                Snippet = "Here it is",
                IsUnread = false,
            },
        };

        _providerMock.Setup(p => p.SearchEmailsAsync(It.IsAny<EmailSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emails);

        var result = await SearchEmailsTool.SearchEmails(_providerMock.Object, query: "test");
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("Subject").GetString().Should().Be("Found it");
    }
}
