using Microsoft.Extensions.Caching.Hybrid;
using Tenantry.Caching.Internal;

namespace Tenantry.Caching;

/// <summary>
/// The <see cref="HybridCache"/> for entries every tenant shares (exchange rates, reference data), where
/// <c>IsolateCaches()</c> makes the injected <see cref="HybridCache"/> keep entries per tenant.
/// </summary>
/// <remarks>
/// Inject it where sharing is meant, so the constructor says so. Its keys and tags are its own: they never name a
/// tenant's entry, and a tenant's never name one of these. It is the <see cref="HybridCache"/> registered before
/// <c>IsolateCaches()</c>, with its serializers and options, and works with or without a current tenant. A factory here
/// runs with no current tenant, whoever calls, so what it loads cannot be one tenant's data: a query through a tenant's
/// <c>DbContext</c> fails, as it does outside a tenant, rather than caching that tenant's rows for every tenant. It
/// runs on the thread pool, without the caller's async context (its activity and log scopes too). A key or tag of
/// <c>*</c> (every entry) means every shared entry. For a keyed <see cref="HybridCache"/>, inject
/// <c>[FromKeyedServices(key)] SharedHybridCache</c> with the same key.
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // A closure of one type per T, not a state type that wraps TState, which Native AOT could not compile were the
        // inner cache this type again.
        Func<CancellationToken, ValueTask<T>> withoutTenant = token => WithoutTenant(state, factory, token);

        return _inner.GetOrCreateAsync(TenantCacheKeys.Shared(key), withoutTenant, options, Tags(tags), cancellationToken);
    }

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

    /// <summary>Removes the shared entries <paramref name="keys"/>; null removes none.</summary>
    /// <param name="keys">The entries' keys, as they were written here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entries are removed.</returns>
    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        // HybridCache's contract treats a null collection as empty, whatever the parameter's annotation says.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        keys is null ? ValueTask.CompletedTask : _inner.RemoveAsync(keys.Select(TenantCacheKeys.Shared).ToList(), cancellationToken);

    /// <summary>Removes the shared entries tagged <paramref name="tag"/>; <c>*</c> removes every shared entry.</summary>
    /// <param name="tag">The tag, as entries were written with it here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entries are removed.</returns>
    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        _inner.RemoveByTagAsync(SharedTag(tag), cancellationToken);

    /// <summary>Removes the shared entries tagged with any of <paramref name="tags"/>; null removes none.</summary>
    /// <param name="tags">The tags, as entries were written with them here.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the entries are removed.</returns>
    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
        // HybridCache's contract treats a null collection as empty, whatever the parameter's annotation says.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        tags is null ? ValueTask.CompletedTask : _inner.RemoveByTagAsync(tags.Select(SharedTag).ToList(), cancellationToken);

    /// <summary>Disposes the cache it wraps, when that cache was created for it and is disposable.</summary>
    public void Dispose()
    {
        if (_ownsInner && _inner is IDisposable disposable)
            disposable.Dispose();
    }

    // Every shared entry carries the shared tag, which "*" (every entry, to HybridCache) removes.
    private static List<string> Tags(IEnumerable<string>? tags) =>
        [TenantCacheKeys.AllSharedTag, .. tags?.Select(TenantCacheKeys.Shared) ?? []];

    private static string SharedTag(string tag) =>
        tag == TenantCacheKeys.EveryEntry ? TenantCacheKeys.AllSharedTag : TenantCacheKeys.Shared(tag);

    // On the thread pool with the caller's async context left behind, so no tenant is current: an inner cache may run
    // the factory in the caller's context (Microsoft's does, for a call whose token cannot be cancelled).
    private static ValueTask<T> WithoutTenant<TState, T>(
        TState state, Func<TState, CancellationToken, ValueTask<T>> factory, CancellationToken token)
    {
        if (ExecutionContext.IsFlowSuppressed())
            return new ValueTask<T>(Task.Run(() => factory(state, token).AsTask(), token));

        using (ExecutionContext.SuppressFlow())
        {
            return new ValueTask<T>(Task.Run(() => factory(state, token).AsTask(), token));
        }
    }
}
