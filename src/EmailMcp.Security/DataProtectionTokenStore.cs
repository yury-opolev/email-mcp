using EmailMcp.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace EmailMcp.Security;

/// <summary>
/// Token store that encrypts data using ASP.NET Data Protection API.
/// On Windows, this automatically uses DPAPI. On Linux/macOS, it uses
/// file-system-based key storage.
/// </summary>
public sealed class DataProtectionTokenStore : ITokenStore
{
    private const string Purpose = "EmailMcp.TokenStore.v1";

    private readonly IDataProtector _protector;
    private readonly string _storageDirectory;
    private readonly ILogger<DataProtectionTokenStore> _logger;

    public DataProtectionTokenStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DataProtectionTokenStore> logger,
        string? storageDirectory = null)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _logger = logger;
        _storageDirectory = storageDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".email-mcp",
                "tokens");

        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task SaveTokenAsync(string key, string tokenData, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenData);

        var encryptedData = _protector.Protect(tokenData);
        var filePath = GetFilePath(key);

        await File.WriteAllTextAsync(filePath, encryptedData, cancellationToken);
        _logger.LogDebug("Token saved for key '{Key}'", key);
    }

    public async Task<string?> LoadTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No token found for key '{Key}'", key);
            return null;
        }

        try
        {
            var encryptedData = await File.ReadAllTextAsync(filePath, cancellationToken);
            return _protector.Unprotect(encryptedData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt token for key '{Key}'. Token may be corrupted", key);
            return null;
        }
    }

    public Task DeleteTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Token deleted for key '{Key}'", key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(File.Exists(GetFilePath(key)));
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_storageDirectory, $"{safeKey}.enc");
    }
}
