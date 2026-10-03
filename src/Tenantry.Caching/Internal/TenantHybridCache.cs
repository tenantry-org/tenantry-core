using Microsoft.Extensions.Caching.Hybrid;

namespace Tenantry.Caching.Internal;

/// <summary>
/// The <see cref="HybridCache"/> an application injects once <c>IsolateCaches()</c> is called: each key and tag goes to
/// the cache it wraps under the current tenant's prefix, and each entry it writes is tagged with the tenant, so
/// invalidating the tenant removes it. A factory runs as the tenant that called: HybridCache runs it without the caller's
/// async context, which is where the current tenant lives. Without a tenant, every call throws
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
            using (tenant.Use())
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

    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var prefix = currentTenant.Require(Name).Prefix;
        return inner.RemoveAsync(keys.Select(key => prefix + key).ToList(), cancellationToken);
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return inner.RemoveByTagAsync(currentTenant.Require(Name).Prefix + tag, cancellationToken);
    }

    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var prefix = currentTenant.Require(Name).Prefix;
        return inner.RemoveByTagAsync(tags.Select(tag => prefix + tag).ToList(), cancellationToken);
    }

    // The tenant's own tags, its prefix stripped of the separator (the tenant), and the tag every tenant entry has.
    private static List<string> Tags(string prefix, IEnumerable<string>? tags)
    {
        List<string> tenantTags = [prefix[..^1], TenantCacheKeys.AllTenantsTag];

        if (tags is not null)
            tenantTags.AddRange(tags.Select(tag => prefix + (tag ?? throw new ArgumentException("A tag is null.", nameof(tags)))));

        return tenantTags;
    }
}
