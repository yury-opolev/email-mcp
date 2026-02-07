using EmailMcp.Gmail;
using FluentAssertions;
using Google.Apis.Gmail.v1.Data;

namespace EmailMcp.Gmail.Tests;

public class GmailMapperTests
{
    [Fact]
    public void ToEmailMessage_MapsBasicHeaders()
    {
        var message = CreateGmailMessage(
            id: "msg-1",
            threadId: "thread-1",
            subject: "Test Subject",
            from: "John Doe <john@example.com>",
            to: "jane@example.com",
            date: "Mon, 15 Jun 2025 10:30:00 +0000");

        var result = GmailMapper.ToEmailMessage(message);

        result.Id.Should().Be("msg-1");
        result.ThreadId.Should().Be("thread-1");
        result.Subject.Should().Be("Test Subject");
        result.From!.Address.Should().Be("john@example.com");
        result.From.DisplayName.Should().Be("John Doe");
        result.To.Should().ContainSingle().Which.Address.Should().Be("jane@example.com");
    }

    [Fact]
    public void ToEmailMessage_DetectsUnreadStatus()
    {
        var message = CreateGmailMessage("msg-2", "thread-2");
        message.LabelIds = new List<string> { "INBOX", "UNREAD" };

        var result = GmailMapper.ToEmailMessage(message);

        result.IsUnread.Should().BeTrue();
    }

    [Fact]
    public void ToEmailMessage_ReadMessageIsNotUnread()
    {
        var message = CreateGmailMessage("msg-3", "thread-3");
        message.LabelIds = new List<string> { "INBOX" };

        var result = GmailMapper.ToEmailMessage(message);

        result.IsUnread.Should().BeFalse();
    }

    [Fact]
    public void ToEmailMessage_ExtractsPlainTextBody()
    {
        var message = CreateGmailMessage("msg-4", "thread-4");
        message.Payload = new MessagePart
        {
            MimeType = "text/plain",
            Headers = new List<MessagePartHeader>(),
            Body = new MessagePartBody
            {
                Data = Base64UrlEncode("Hello, World!"),
            },
        };

        var result = GmailMapper.ToEmailMessage(message, includeBody: true);

        result.Body.Should().Be("Hello, World!");
    }

    [Fact]
    public void ToEmailMessage_ExtractsAttachments()
    {
        var message = CreateGmailMessage("msg-5", "thread-5");
        message.Payload = new MessagePart
        {
            Headers = new List<MessagePartHeader>(),
            Parts = new List<MessagePart>
            {
                new()
                {
                    Filename = "report.pdf",
                    MimeType = "application/pdf",
                    Body = new MessagePartBody { Size = 1024, AttachmentId = "att-1" },
                },
                new()
                {
                    Filename = "",
                    MimeType = "text/plain",
                    Body = new MessagePartBody { Data = Base64UrlEncode("Body text") },
                },
            },
        };

        var result = GmailMapper.ToEmailMessage(message);

        result.Attachments.Should().ContainSingle();
        result.Attachments[0].Filename.Should().Be("report.pdf");
        result.Attachments[0].MimeType.Should().Be("application/pdf");
        result.Attachments[0].Size.Should().Be(1024);
    }

    [Fact]
    public void ToEmailMessage_HandlesMissingPayload()
    {
        var message = new Message { Id = "msg-6", ThreadId = "thread-6" };

        var result = GmailMapper.ToEmailMessage(message);

        result.Subject.Should().BeNull();
        result.From.Should().BeNull();
        result.Body.Should().BeNull();
    }

    [Fact]
    public void ParseEmailAddress_ParsesDisplayNameAndAddress()
    {
        var result = GmailMapper.ParseEmailAddress("John Doe <john@example.com>");

        result.Should().NotBeNull();
        result!.Address.Should().Be("john@example.com");
        result.DisplayName.Should().Be("John Doe");
    }

    [Fact]
    public void ParseEmailAddress_ParsesPlainAddress()
    {
        var result = GmailMapper.ParseEmailAddress("john@example.com");

        result.Should().NotBeNull();
        result!.Address.Should().Be("john@example.com");
    }

    [Fact]
    public void ParseEmailAddress_ReturnsNullForEmpty()
    {
        GmailMapper.ParseEmailAddress(null).Should().BeNull();
        GmailMapper.ParseEmailAddress("").Should().BeNull();
        GmailMapper.ParseEmailAddress("   ").Should().BeNull();
    }

    [Fact]
    public void ParseEmailAddresses_ParsesMultipleAddresses()
    {
        var result = GmailMapper.ParseEmailAddresses("alice@test.com, Bob <bob@test.com>");

        result.Should().HaveCount(2);
        result[0].Address.Should().Be("alice@test.com");
        result[1].Address.Should().Be("bob@test.com");
    }

    [Fact]
    public void ToEmailMessage_ParsesMultipleCcRecipients()
    {
        var message = CreateGmailMessage("msg-7", "thread-7",
            cc: "alice@test.com, bob@test.com");

        var result = GmailMapper.ToEmailMessage(message);

        result.Cc.Should().HaveCount(2);
    }

    private static Message CreateGmailMessage(
        string id,
        string threadId,
        string? subject = null,
        string? from = null,
        string? to = null,
        string? cc = null,
        string? date = null)
    {
        var headers = new List<MessagePartHeader>();
        if (subject is not null) headers.Add(new MessagePartHeader { Name = "Subject", Value = subject });
        if (from is not null) headers.Add(new MessagePartHeader { Name = "From", Value = from });
        if (to is not null) headers.Add(new MessagePartHeader { Name = "To", Value = to });
        if (cc is not null) headers.Add(new MessagePartHeader { Name = "Cc", Value = cc });
        if (date is not null) headers.Add(new MessagePartHeader { Name = "Date", Value = date });

        return new Message
        {
            Id = id,
            ThreadId = threadId,
            Snippet = "Preview text...",
            Payload = new MessagePart { Headers = headers },
        };
    }

    private static string Base64UrlEncode(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
