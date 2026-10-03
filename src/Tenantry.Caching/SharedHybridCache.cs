using Microsoft.Extensions.Caching.Hybrid;
using Tenantry.Caching.Internal;

namespace Tenantry.Caching;

/// <summary>
/// The <see cref="HybridCache"/> for entries every tenant shares (exchange rates, reference data), where
/// <c>IsolateCaches()</c> makes the injected <see cref="HybridCache"/> keep entries per tenant. Inject it where sharing
/// is meant, so the constructor says so. Its keys and tags are its own: they never name a tenant's entry, and a tenant's
/// never name one of these.
/// </summary>
/// <remarks>
/// It is the <see cref="HybridCache"/> registered before <c>IsolateCaches()</c>, with its serializers and options, and
/// works with or without a current tenant. Microsoft's HybridCache runs a factory without the caller's async context,
/// so a factory here runs with no current tenant: load data that no tenant owns.
/// </remarks>
/// <example>
/// <code>
/// public sealed class ExchangeRates(SharedHybridCache cache)
/// {
///     public ValueTask&lt;decimal&gt; GetAsync(string currency, CancellationToken ct) =&gt;
///         cache.GetOrCreateAsync($"fx:{currency}", async token =&gt; await LoadRateAsync(currency, token), cancellationToken: ct);
/// }
/// </code>
/// </example>
public sealed class SharedHybridCache : HybridCache, IDisposable
{
    private readonly HybridCache _inner;
    private readonly bool _ownsInner;

    internal SharedHybridCache(HybridCache inner, bool ownsInner)
    {
        _inner = inner;
        _ownsInner = ownsInner;
    }

    /// <summary>The cache this one names its entries in, which the tenants' cache uses too.</summary>
    internal HybridCache Inner => _inner;

    /// <inheritdoc />
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        _inner.GetOrCreateAsync(TenantCacheKeys.Shared(key), state, factory, options, Tags(tags), cancellationToken);

    /// <inheritdoc />
    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        _inner.SetAsync(TenantCacheKeys.Shared(key), value, options, Tags(tags), cancellationToken);

    /// <summary>Removes the shared entry <paramref name="key"/>.</summary>
    /// <param name="key">The entry's key, as it was written here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entry is removed.</returns>
    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.RemoveAsync(TenantCacheKeys.Shared(key), cancellationToken);

    /// <summary>Removes the shared entries <paramref name="keys"/>.</summary>
    /// <param name="keys">The entries' keys, as they were written here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entries are removed.</returns>
    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return _inner.RemoveAsync(keys.Select(TenantCacheKeys.Shared).ToList(), cancellationToken);
    }

    /// <summary>Removes the shared entries tagged <paramref name="tag"/>.</summary>
    /// <param name="tag">The tag, as entries were written with it here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entries are removed.</returns>
    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        _inner.RemoveByTagAsync(TenantCacheKeys.Shared(tag), cancellationToken);

    /// <summary>Removes the shared entries tagged with any of <paramref name="tags"/>.</summary>
    /// <param name="tags">The tags, as entries were written with them here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entries are removed.</returns>
    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return _inner.RemoveByTagAsync(tags.Select(TenantCacheKeys.Shared).ToList(), cancellationToken);
    }

    /// <summary>Disposes the cache it wraps, when that cache was created for it and is disposable.</summary>
    public void Dispose()
    {
        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();
    }

    private static List<string>? Tags(IEnumerable<string>? tags) => tags?.Select(TenantCacheKeys.Shared).ToList();
}
