using EmailMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EmailMcp.Gmail;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Gmail as the email provider with OAuth 2.0 authentication.
    /// </summary>
    /// <remarks>
    /// Providers and authenticators are not registered directly, because each one is bound to a
    /// single account. They are created and cached per account alias by
    /// <see cref="GmailAccountRegistry"/>, which tools resolve instead.
    /// </remarks>
    public static IServiceCollection AddGmailProvider(
        this IServiceCollection services,
        Action<GmailOptions>? configure = null)
    {
        var options = new GmailOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<AccountIndexStore>();
        services.AddSingleton<GmailAccountRegistry>();
        services.AddSingleton<IAccountRegistry>(sp => sp.GetRequiredService<GmailAccountRegistry>());

        return services;
    }
}
