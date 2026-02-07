using System.Text.Json;
using EmailMcp.Abstractions;
using EmailMcp.Server.Tools;
using FluentAssertions;
using Moq;

namespace EmailMcp.Server.Tests;

public class ListLabelsToolTests
{
    private readonly Mock<IEmailProvider> _providerMock = new();

    [Fact]
    public async Task ListLabels_ReturnsLabelsAsJson()
    {
        var labels = new List<EmailLabel>
        {
            new() { Id = "INBOX", Name = "INBOX", Type = "system", UnreadCount = 5, TotalCount = 100 },
            new() { Id = "SENT", Name = "SENT", Type = "system" },
            new() { Id = "Label_1", Name = "Work", Type = "user" },
        };

        _providerMock.Setup(p => p.ListLabelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(labels);

        var result = await ListLabelsTool.ListLabels(_providerMock.Object);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetArrayLength().Should().Be(3);
        doc.RootElement[0].GetProperty("Id").GetString().Should().Be("INBOX");
        doc.RootElement[0].GetProperty("Name").GetString().Should().Be("INBOX");
        doc.RootElement[2].GetProperty("Name").GetString().Should().Be("Work");
    }
}
