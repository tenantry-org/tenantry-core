using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Hybrid;

namespace Tenantry.Caching.Tests;

/// <summary>A HybridCache of the tests' own: entries by key, with their tags, and the calls it received.</summary>
internal sealed class InMemoryHybridCache : HybridCache, IDisposable
{
    public ConcurrentDictionary<string, (object? Value, string[] Tags)> Entries { get; } = new();

    public bool Disposed { get; private set; }

    public override async ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (Entries.TryGetValue(key, out var entry))
            return (T)entry.Value!;

        // As Microsoft's HybridCache does, the factory runs without the caller's async context.
        Task<T> run;
        using (ExecutionContext.SuppressFlow())
        {
            run = Task.Run(() => factory(state, cancellationToken).AsTask(), cancellationToken);
        }

        var value = await run;
        Entries[key] = (value, tags?.ToArray() ?? []);
        return value;
    }

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        Entries[key] = (value, tags?.ToArray() ?? []);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Entries.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        foreach (var pair in Entries.Where(pair => pair.Value.Tags.Contains(tag)))
            Entries.TryRemove(pair);

        return ValueTask.CompletedTask;
    }

    public void Dispose() => Disposed = true;
}
