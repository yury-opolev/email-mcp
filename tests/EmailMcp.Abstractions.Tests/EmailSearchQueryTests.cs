using EmailMcp.Abstractions;
using FluentAssertions;

namespace EmailMcp.Abstractions.Tests;

public class EmailSearchQueryTests
{
    [Fact]
    public void DefaultMaxResults_IsTwenty()
    {
        var query = new EmailSearchQuery();

        query.MaxResults.Should().Be(20);
    }

    [Fact]
    public void AllProperties_DefaultToNull()
    {
        var query = new EmailSearchQuery();

        query.Query.Should().BeNull();
        query.From.Should().BeNull();
        query.To.Should().BeNull();
        query.Subject.Should().BeNull();
        query.After.Should().BeNull();
        query.Before.Should().BeNull();
        query.LabelId.Should().BeNull();
    }
}
