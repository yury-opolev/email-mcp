using EmailMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EmailMcp.Gmail;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Gmail as the email provider with OAuth 2.0 authentication.
    /// </summary>
    public static IServiceCollection AddGmailProvider(
        this IServiceCollection services,
        Action<GmailOptions>? configure = null)
    {
        var options = new GmailOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<GmailAuthenticator>();
        services.AddSingleton<IEmailAuthenticator>(sp => sp.GetRequiredService<GmailAuthenticator>());
        services.AddSingleton<IEmailProvider, GmailEmailProvider>();

        return services;
    }
}
