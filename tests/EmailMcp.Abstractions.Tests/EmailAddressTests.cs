using EmailMcp.Abstractions;
using FluentAssertions;

namespace EmailMcp.Abstractions.Tests;

public class EmailAddressTests
{
    [Fact]
    public void ToString_WithDisplayName_ReturnsFormattedString()
    {
        var address = new EmailAddress("john@example.com", "John Doe");

        address.ToString().Should().Be("John Doe <john@example.com>");
    }

    [Fact]
    public void ToString_WithoutDisplayName_ReturnsAddressOnly()
    {
        var address = new EmailAddress("john@example.com");

        address.ToString().Should().Be("john@example.com");
    }

    [Fact]
    public void ToString_WithEmptyDisplayName_ReturnsAddressOnly()
    {
        var address = new EmailAddress("john@example.com", "");

        address.ToString().Should().Be("john@example.com");
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        var a = new EmailAddress("john@example.com", "John");
        var b = new EmailAddress("john@example.com", "John");

        a.Should().Be(b);
    }

    [Fact]
    public void Record_Inequality_WorksCorrectly()
    {
        var a = new EmailAddress("john@example.com", "John");
        var b = new EmailAddress("jane@example.com", "Jane");

        a.Should().NotBe(b);
    }
}
