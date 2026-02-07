using EmailMcp.Abstractions;
using FluentAssertions;

namespace EmailMcp.Abstractions.Tests;

public class EmailMessageTests
{
    [Fact]
    public void EmailMessage_DefaultCollections_AreEmpty()
    {
        var message = new EmailMessage
        {
            Id = "123",
            ThreadId = "thread-123",
        };

        message.To.Should().BeEmpty();
        message.Cc.Should().BeEmpty();
        message.Bcc.Should().BeEmpty();
        message.LabelIds.Should().BeEmpty();
        message.Attachments.Should().BeEmpty();
    }

    [Fact]
    public void EmailMessage_CanBeCreatedWithAllProperties()
    {
        var from = new EmailAddress("sender@test.com", "Sender");
        var to = new EmailAddress("recipient@test.com", "Recipient");

        var message = new EmailMessage
        {
            Id = "msg-1",
            ThreadId = "thread-1",
            Subject = "Test Subject",
            From = from,
            To = [to],
            Date = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero),
            Snippet = "A short preview...",
            Body = "Full body text",
            IsUnread = true,
            LabelIds = ["INBOX", "UNREAD"],
        };

        message.Id.Should().Be("msg-1");
        message.Subject.Should().Be("Test Subject");
        message.From.Should().Be(from);
        message.To.Should().ContainSingle().Which.Should().Be(to);
        message.IsUnread.Should().BeTrue();
        message.LabelIds.Should().HaveCount(2);
    }
}
