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
                return await factory(state, token);
            }
        };

        return inner.GetOrCreateAsync(tenant.Prefix + key, asTheTenant, options, Tags(tenant.Prefix, tags), cancellationToken);
    }

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var prefix = currentTenant.Require(Name).Prefix;
        return inner.SetAsync(prefix + key, value, options, Tags(prefix, tags), cancellationToken);
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return inner.RemoveAsync(currentTenant.Require(Name).Prefix + key, cancellationToken);
    }

    // HybridCache's contract treats a null collection as empty.
    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var prefix = currentTenant.Require(Name).Prefix;
        return keys is null ? ValueTask.CompletedTask : inner.RemoveAsync(keys.Select(key => prefix + key).ToList(), cancellationToken);
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return inner.RemoveByTagAsync(TenantTag(currentTenant.Require(Name).Prefix, tag), cancellationToken);
    }

    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var prefix = currentTenant.Require(Name).Prefix;
        return tags is null ? ValueTask.CompletedTask : inner.RemoveByTagAsync(tags.Select(tag => TenantTag(prefix, tag)).ToList(), cancellationToken);
    }

    // "*", every entry to HybridCache, is every entry of the tenant: its own tag, the prefix without the separator.
    private static string TenantTag(string prefix, string tag) => tag == TenantCacheKeys.EveryEntry ? prefix[..^1] : prefix + tag;

    // The tenant's own tags, its prefix stripped of the separator (the tenant), and the tag every tenant entry has.
    private static List<string> Tags(string prefix, IEnumerable<string>? tags)
    {
        List<string> tenantTags = [prefix[..^1], TenantCacheKeys.AllTenantsTag];

        foreach (var tag in tags ?? [])
            tenantTags.Add(prefix + (tag ?? throw new ArgumentException("A tag is null.", nameof(tags))));

        return tenantTags;
    }
}
