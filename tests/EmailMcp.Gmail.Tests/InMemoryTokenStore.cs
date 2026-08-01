using System.Collections.Concurrent;
using EmailMcp.Abstractions;

namespace EmailMcp.Gmail.Tests;

/// <summary>
/// In-memory <see cref="ITokenStore"/> for tests, so the registry can be exercised without disk
/// or encryption.
/// </summary>
internal sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, string> values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => this.values.Keys.ToList();

    public Task SaveTokenAsync(string key, string tokenData, CancellationToken cancellationToken = default)
    {
        this.values[key] = tokenData;
        return Task.CompletedTask;
    }

    public Task<string?> LoadTokenAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.values.TryGetValue(key, out var value) ? value : null);

    public Task DeleteTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        this.values.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(this.values.ContainsKey(key));
}
