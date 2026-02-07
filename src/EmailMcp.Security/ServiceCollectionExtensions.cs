using EmailMcp.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace EmailMcp.Security;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers encrypted token storage using ASP.NET Data Protection.
    /// On Windows, DPAPI is used automatically. On Linux/macOS, file-based key storage is used.
    /// </summary>
    public static IServiceCollection AddEmailSecurity(
        this IServiceCollection services,
        string? tokenStorageDirectory = null)
    {
        var keysDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".email-mcp",
            "keys");

        Directory.CreateDirectory(keysDirectory);

        var dpBuilder = services.AddDataProtection()
            .SetApplicationName("EmailMcp")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        if (OperatingSystem.IsWindows())
        {
            dpBuilder.ProtectKeysWithDpapi(protectToLocalMachine: false);
        }

        services.AddSingleton<ITokenStore>(sp =>
            new DataProtectionTokenStore(
                sp.GetRequiredService<IDataProtectionProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DataProtectionTokenStore>>(),
                tokenStorageDirectory));

        return services;
    }
}
