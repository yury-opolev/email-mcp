using EmailMcp.Abstractions;
using EmailMcp.Security;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmailMcp.Security.Tests;

public class DataProtectionTokenStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly DataProtectionTokenStore _store;

    public DataProtectionTokenStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "email-mcp-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("EmailMcp.Tests");
        var sp = services.BuildServiceProvider();
        var dataProtectionProvider = sp.GetRequiredService<IDataProtectionProvider>();

        _store = new DataProtectionTokenStore(
            dataProtectionProvider,
            NullLogger<DataProtectionTokenStore>.Instance,
            _testDirectory);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips_Successfully()
    {
        const string key = "test-token";
        const string tokenData = """{"access_token":"ya29.abc","refresh_token":"1//xyz"}""";

        await _store.SaveTokenAsync(key, tokenData);
        var loaded = await _store.LoadTokenAsync(key);

        loaded.Should().Be(tokenData);
    }

    [Fact]
    public async Task SaveToken_EncryptsDataOnDisk()
    {
        const string key = "test-encrypt";
        const string tokenData = "sensitive-secret-data";

        await _store.SaveTokenAsync(key, tokenData);

        var filePath = Path.Combine(_testDirectory, $"{key}.enc");
        File.Exists(filePath).Should().BeTrue();

        var rawContent = await File.ReadAllTextAsync(filePath);
        rawContent.Should().NotBe(tokenData, "token should be encrypted on disk");
        rawContent.Should().NotContain("sensitive", "encrypted data should not contain plaintext");
    }

    [Fact]
    public async Task LoadToken_ReturnsNull_WhenKeyDoesNotExist()
    {
        var result = await _store.LoadTokenAsync("nonexistent-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteToken_RemovesToken()
    {
        const string key = "to-delete";
        await _store.SaveTokenAsync(key, "some-data");

        await _store.DeleteTokenAsync(key);

        var result = await _store.LoadTokenAsync(key);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForExistingToken()
    {
        const string key = "exists-test";
        await _store.SaveTokenAsync(key, "data");

        var exists = await _store.ExistsAsync(key);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseForMissingToken()
    {
        var exists = await _store.ExistsAsync("missing");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SaveToken_OverwritesExistingToken()
    {
        const string key = "overwrite-test";

        await _store.SaveTokenAsync(key, "original-data");
        await _store.SaveTokenAsync(key, "updated-data");

        var loaded = await _store.LoadTokenAsync(key);
        loaded.Should().Be("updated-data");
    }

    [Fact]
    public async Task SaveToken_ThrowsOnNullKey()
    {
        var act = () => _store.SaveTokenAsync(null!, "data");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveToken_ThrowsOnEmptyData()
    {
        var act = () => _store.SaveTokenAsync("key", "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }
        catch { /* cleanup best-effort */ }
    }
}
