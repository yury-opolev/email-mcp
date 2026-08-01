using EmailMcp.Abstractions;
using Moq;

namespace EmailMcp.Server.Tests;

/// <summary>
/// Builds an <see cref="IAccountRegistry"/> that resolves to a single fixed account, so tool
/// tests can keep asserting against a provider or authenticator mock directly.
/// </summary>
internal static class TestRegistry
{
    public const string DefaultAlias = "default";

    /// <summary>A registry whose single account resolves to the given provider.</summary>
    public static IAccountRegistry For(IEmailProvider provider)
    {
        var registry = NewRegistry();
        registry
            .Setup(r => r.GetProviderAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        return registry.Object;
    }

    /// <summary>A registry whose single account resolves to the given authenticator.</summary>
    public static IAccountRegistry For(IEmailAuthenticator authenticator)
    {
        var registry = NewRegistry();
        registry
            .Setup(r => r.GetAuthenticatorAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authenticator);
        return registry.Object;
    }

    private static Mock<IAccountRegistry> NewRegistry()
    {
        var registry = new Mock<IAccountRegistry>();

        registry
            .Setup(r => r.ResolveAliasAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultAlias);

        registry
            .Setup(r => r.TryRecordAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return registry;
    }
}
