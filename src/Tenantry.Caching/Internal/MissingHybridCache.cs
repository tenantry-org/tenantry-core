using Microsoft.Extensions.Caching.Hybrid;

namespace Tenantry.Caching.Internal;

/// <summary>
/// The <see cref="HybridCache"/> registered when <c>IsolateCaches()</c> finds none to isolate: every call throws,
/// naming the fix. A later registration that only adds a cache if none exists (<c>AddHybridCache()</c>'s) finds this one
/// in its place, so the application fails loudly rather than caching unisolated; one that replaces registrations
/// (<c>AddSingleton</c>) is not caught.
/// </summary>
internal sealed class MissingHybridCache : HybridCache
{
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) => throw Missing();

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) => throw Missing();

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => throw Missing();

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => throw Missing();

    private static InvalidOperationException Missing() =>
        new("IsolateCaches() found no HybridCache to isolate when it ran, so HybridCache is not available. Register it " +
            "before AddTenantry: builder.Services.AddHybridCache(), then builder.Services.AddTenantry(...).");
}
