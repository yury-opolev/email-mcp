using EmailMcp.Abstractions;
using FluentAssertions;

namespace EmailMcp.Abstractions.Tests;

public class AccountKeysTests
{
    /// <summary>
    /// Mirrors the sanitisation in DataProtectionTokenStore.GetFilePath, which is what turns a
    /// key into a filename.
    /// </summary>
    private static string AsFilename(string key) =>
        string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [Fact]
    public void Keys_ContainNoCharactersThatFilenameSanitisationWouldReplace()
    {
        var keys = new[]
        {
            AccountKeys.Index,
            AccountKeys.ClientCredentials("studio"),
            AccountKeys.OAuthToken("studio"),
            AccountKeys.OAuthTokenPrefix("studio"),
        };

        foreach (var key in keys)
        {
            AsFilename(key).Should().Be(key, "keys must survive filename sanitisation unchanged");
        }
    }

    [Fact]
    public void DistinctAliases_ProduceDistinctFilenames()
    {
        var first = AsFilename(AccountKeys.OAuthToken("work"));
        var second = AsFilename(AccountKeys.OAuthToken("work-2"));

        first.Should().NotBe(second);
    }

    [Fact]
    public void ClientCredentialsAndToken_DoNotCollideForTheSameAlias()
    {
        AccountKeys.ClientCredentials("studio")
            .Should().NotBe(AccountKeys.OAuthToken("studio"));
    }

    [Fact]
    public void OAuthTokenKey_MatchesWhatTheGoogleDataStoreAdapterWrites()
    {
        // The adapter appends "-{userId}" to the prefix, with userId "user".
        var written = $"{AccountKeys.OAuthTokenPrefix("studio")}-user";

        written.Should().Be(AccountKeys.OAuthToken("studio"));
    }
}
