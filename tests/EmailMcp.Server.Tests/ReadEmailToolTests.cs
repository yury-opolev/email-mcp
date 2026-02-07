using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class ReadEmailToolTests
{
    private readonly Mock<IEmailProvider> _providerMock = new();

    [Fact]
    public async Task ReadEmail_ReturnsFullEmailAsJson()
    {
        var email = new EmailMessage
        {
            Id = "msg-1",
            ThreadId = "thread-1",
            Subject = "Hello",
            From = new EmailAddress("alice@test.com", "Alice"),
            To = [new EmailAddress("bob@test.com", "Bob")],
            Date = new DateTimeOffset(2025, 6, 15, 14, 0, 0, TimeSpan.Zero),
            Body = "This is the email body.",
            IsUnread = false,
            LabelIds = ["INBOX"],
            Attachments =
            [
                new EmailAttachment
                {
                    Filename = "doc.pdf",
                    MimeType = "application/pdf",
                    Size = 2048,
                },
            ],
        };

        _providerMock.Setup(p => p.GetEmailAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(email);

        var result = await ReadEmailTool.ReadEmail(_providerMock.Object, "msg-1");
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("Subject").GetString().Should().Be("Hello");
        doc.RootElement.GetProperty("Body").GetString().Should().Contain("email body");
        doc.RootElement.GetProperty("Attachments").GetArrayLength().Should().Be(1);
    }
}
