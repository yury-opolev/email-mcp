using EmailMcp.Abstractions;
using FluentAssertions;

namespace EmailMcp.Abstractions.Tests;

public class AccountAliasTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("studio")]
    [InlineData("work-2")]
    [InlineData("a")]
    public void IsValid_AcceptsPlainAliases(string alias) =>
        AccountAlias.IsValid(alias).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("has space")]
    [InlineData("has.dot")]
    [InlineData("has@at")]
    [InlineData("index")]
    public void IsValid_RejectsBadAliases(string? alias) =>
        AccountAlias.IsValid(alias).Should().BeFalse();

    [Fact]
    public void IsValid_RejectsUnderscore_BecauseKeySanitisationUsesIt()
    {
        // Filename sanitisation replaces invalid characters with '_'. Allowing '_' in an alias
        // would reopen the collision this charset exists to prevent.
        AccountAlias.IsValid("has_underscore").Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsOverlongAlias() =>
        AccountAlias.IsValid(new string('a', AccountAlias.MaxLength + 1)).Should().BeFalse();

    [Fact]
    public void IsValid_AcceptsAliasAtMaxLength() =>
        AccountAlias.IsValid(new string('a', AccountAlias.MaxLength)).Should().BeTrue();

    [Theory]
    [InlineData("  Studio  ", "studio")]
    [InlineData("DEFAULT", "default")]
    public void Normalize_TrimsAndLowercases(string input, string expected) =>
        AccountAlias.Normalize(input).Should().Be(expected);
}
