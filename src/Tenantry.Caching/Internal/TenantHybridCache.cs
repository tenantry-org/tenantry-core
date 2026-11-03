using Microsoft.Extensions.Caching.Hybrid;

namespace Tenantry.Caching.Internal;

/// <summary>
/// The <see cref="HybridCache"/> an application injects once <c>IsolateCaches()</c> is called: each key and tag goes to
/// the cache it wraps under the current tenant's prefix, and each entry it writes is tagged with the tenant, so
/// invalidating the tenant removes it. A factory runs as the tenant that called: a HybridCache may run it without the
/// caller's async context, where the current tenant lives (Microsoft's does, for a call whose token can be cancelled).
/// The tag <c>*</c> (every entry) means every entry of the tenant. Without a tenant, every call throws
/// <see cref="TenantNotResolvedException"/>.
/// </summary>
internal sealed class TenantHybridCache(HybridCache inner, ICurrentTenant currentTenant) : HybridCache
{
    private const string Name = "HybridCache";

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        var tenant = currentTenant.Require(Name);

        // A closure of one type per T, not a state type that wraps TState: the inner cache could be this one again, and
        // a wrapped state would grow a new generic instantiation each time, which Native AOT cannot compile.
        Func<CancellationToken, ValueTask<T>> asTheTenant = async token =>
        {
            using (tenant.MakeCurrent())
            {
                return await factory(state, token).ConfigureAwait(false);
            }
        };

        return inner.GetOrCreateAsync(tenant.Prefix + key, asTheTenant, options, Tags(tenant, tags), cancellationToken);
    }

#if NET11_0_OR_GREATER
    // The factory may change how its result is cached through the context (a failure for a few seconds, say).
    // HybridCache's own implementation of this overload calls the one above with the caller's options, which would
    // drop those changes, so the context goes on to the inner cache.
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, HybridCacheEntryContext, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        var tenant = currentTenant.Require(Name);

        // A closure of one type per T, as above.
        Func<HybridCacheEntryContext, CancellationToken, ValueTask<T>> asTheTenant = async (context, token) =>
        {
            using (tenant.MakeCurrent())
            {
                return await factory(state, context, token).ConfigureAwait(false);
            }
        };

        return inner.GetOrCreateAsync(tenant.Prefix + key, asTheTenant, options, Tags(tenant, tags), cancellationToken);
    }
#endif

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var tenant = currentTenant.Require(Name);
        return inner.SetAsync(tenant.Prefix + key, value, options, Tags(tenant, tags), cancellationToken);
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return inner.RemoveAsync(currentTenant.Require(Name).Prefix + key, cancellationToken);
    }

    // HybridCache's contract treats a null collection as empty, whatever the parameter's annotation says.
    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var prefix = currentTenant.Require(Name).Prefix;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return keys is null ? ValueTask.CompletedTask : inner.RemoveAsync(keys.Select(key => prefix + key).ToList(), cancellationToken);
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return inner.RemoveByTagAsync(TenantTag(currentTenant.Require(Name), tag), cancellationToken);
    }

    // HybridCache's contract treats a null collection as empty, whatever the parameter's annotation says.
    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var tenant = currentTenant.Require(Name);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return tags is null ? ValueTask.CompletedTask : inner.RemoveByTagAsync(tags.Select(tag => TenantTag(tenant, tag)).ToList(), cancellationToken);
    }

    // "*", every entry to HybridCache, is every entry of the tenant: its own tag.
    private static string TenantTag(ICapturedTenant tenant, string tag) =>
        tag == TenantCacheKeys.EveryEntry ? tenant.Tag : tenant.Prefix + tag;

    // The tenant's tag, the tag every tenant entry has, and the caller's tags under the tenant's prefix. A call with no
    // tags of its own, the usual case, gets the first two, built once per tenant: HybridCache reads them only when it
    // writes an entry.
    private static IEnumerable<string> Tags(ICapturedTenant tenant, IEnumerable<string>? tags)
    {
        if (tags is null)
            return tenant.Tags;

        List<string> tenantTags = [.. tenant.Tags];

        foreach (var tag in tags)
            tenantTags.Add(tenant.Prefix + (tag ?? throw new ArgumentException("A tag is null.", nameof(tags))));

        return tenantTags;
    }
}
