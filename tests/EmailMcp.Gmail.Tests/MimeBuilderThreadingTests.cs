using EmailMcp.Abstractions;
using EmailMcp.Gmail;
using FluentAssertions;

namespace EmailMcp.Gmail.Tests;

public class MimeBuilderThreadingTests
{
    private static SendEmailRequest Base(string? inReplyTo = null, string? references = null) => new()
    {
        To = [new EmailAddress("her@example.com")],
        Subject = "Re: something",
        Body = "hello",
        InReplyTo = inReplyTo,
        References = references,
    };

    [Fact]
    public void Build_EmitsThreadingHeadersWhenSupplied()
    {
        var mime = MimeBuilder.Build(Base(
            inReplyTo: "<abc@mail.example.com>",
            references: "<root@mail.example.com> <abc@mail.example.com>"));

        mime.Should().Contain("In-Reply-To: <abc@mail.example.com>\r\n");
        mime.Should().Contain("References: <root@mail.example.com> <abc@mail.example.com>\r\n");
    }

    [Fact]
    public void Build_OmitsThreadingHeadersEntirelyWhenAbsent()
    {
        // A plain send must not carry empty threading headers - an empty In-Reply-To is worse
        // than none, since some clients treat it as a malformed reference.
        var mime = MimeBuilder.Build(Base());

        mime.Should().NotContain("In-Reply-To:");
        mime.Should().NotContain("References:");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_TreatsBlankThreadingHeadersAsAbsent(string blank)
    {
        var mime = MimeBuilder.Build(Base(inReplyTo: blank, references: blank));

        mime.Should().NotContain("In-Reply-To:");
        mime.Should().NotContain("References:");
    }

    [Fact]
    public void Build_DoesNotRfc2047EncodeMessageIds()
    {
        // Message-IDs are ASCII and angle-bracketed. Running them through the encoded-word
        // path would produce "=?UTF-8?B?...?=" and break threading silently.
        var mime = MimeBuilder.Build(Base(inReplyTo: "<abc@mail.example.com>"));

        mime.Should().NotContain("=?UTF-8?B?");
        mime.Should().Contain("<abc@mail.example.com>");
    }

    [Fact]
    public void Build_PlacesThreadingHeadersBeforeTheBody()
    {
        var mime = MimeBuilder.Build(Base(inReplyTo: "<abc@mail.example.com>"));

        var headerIndex = mime.IndexOf("In-Reply-To:", StringComparison.Ordinal);
        var bodyIndex = mime.IndexOf("hello", StringComparison.Ordinal);

        headerIndex.Should().BeGreaterThan(-1);
        headerIndex.Should().BeLessThan(bodyIndex);
    }
}
