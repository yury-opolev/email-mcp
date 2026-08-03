using EmailMcp.Abstractions;
using FluentAssertions;

namespace EmailMcp.Abstractions.Tests;

public class MailSubjectTests
{
    [Theory]
    [InlineData("Un'app per l'orologio", "Re: Un'app per l'orologio")]
    [InlineData("  padded  ", "Re: padded")]
    public void EnsureReplyPrefix_AddsPrefixWhenAbsent(string subject, string expected)
    {
        MailSubject.EnsureReplyPrefix(subject).Should().Be(expected);
    }

    [Theory]
    [InlineData("Re: already replied")]
    [InlineData("RE: SHOUTING")]
    [InlineData("re: lowercase")]
    public void EnsureReplyPrefix_DoesNotStackEnglishPrefix(string subject)
    {
        MailSubject.EnsureReplyPrefix(subject).Should().Be(subject);
    }

    [Theory]
    [InlineData("AW: deutsche Antwort")]
    [InlineData("SV: svenskt svar")]
    [InlineData("Rif: risposta italiana")]
    [InlineData("Res: resposta")]
    public void EnsureReplyPrefix_RecognisesNonEnglishPrefixes(string subject)
    {
        // Replying to a German or Italian correspondent brings their prefix back with the
        // quoted thread; stacking "Re:" on top of "AW:" is the tell of a careless client.
        MailSubject.EnsureReplyPrefix(subject).Should().Be(subject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureReplyPrefix_HandlesMissingSubject(string? subject)
    {
        // "Re: " with nothing after it reads as broken, so a bare "Re:" is used instead.
        MailSubject.EnsureReplyPrefix(subject).Should().Be("Re:");
    }

    [Fact]
    public void EnsureForwardPrefix_AddsAndDoesNotStack()
    {
        MailSubject.EnsureForwardPrefix("Report").Should().Be("Fwd: Report");
        MailSubject.EnsureForwardPrefix("Fwd: Report").Should().Be("Fwd: Report");
        MailSubject.EnsureForwardPrefix("FW: Report").Should().Be("FW: Report");
    }

    [Fact]
    public void EnsureReplyPrefix_DoesNotTreatUnrelatedWordAsPrefix()
    {
        // "Reminder" starts with "re" but is not a reply prefix - the colon is what matters.
        MailSubject.EnsureReplyPrefix("Reminder: standup").Should().Be("Re: Reminder: standup");
    }
}
