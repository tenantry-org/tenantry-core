using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tenantry.Options.Internal;

/// <summary>
/// The current tenant's id as text, for the options caches, which have no key type: <c>ConfigurePerTenant</c>
/// registers it for the application's.
/// </summary>
internal interface ICurrentTenantId
{
    /// <summary>The current tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>, or null with no tenant.</summary>
    string? Current { get; }
}

internal sealed class CurrentTenantId<TKey>(ITenantContext<TKey> tenantContext) : ICurrentTenantId
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public string? Current => tenantContext.CurrentTenant is { } tenant ? TenantIds.Format(tenant.TenantId) : null;
}

/// <summary>A cache of one options type's values, per tenant, that a tenant's invalidation can clear.</summary>
internal interface ITenantOptionsCache
{
    void Remove(string tenantId);

    void Clear();
}

/// <summary>
/// Every <see cref="TenantOptionsCache{TOptions}"/> created, so invalidating a tenant clears its values of every
/// options type.
/// </summary>
internal sealed class TenantOptionsCaches
{
    private readonly ConcurrentBag<ITenantOptionsCache> _caches = [];

    public void Add(ITenantOptionsCache cache) => _caches.Add(cache);

    public void Remove(string tenantId)
    {
        foreach (var cache in _caches)
            cache.Remove(tenantId);
    }

    public void Clear()
    {
        foreach (var cache in _caches)
            cache.Clear();
    }
}

/// <summary>
/// The <see cref="IOptionsMonitorCache{TOptions}"/> of an options type configured per tenant: each value is kept per
/// tenant (and per options name), so <see cref="IOptionsMonitor{TOptions}"/> and Tenantry's <see cref="IOptions{TOptions}"/>
/// and <see cref="IOptionsSnapshot{TOptions}"/> give the current tenant's. A change to the configuration a name is bound
/// to clears that name for every tenant, whether or not anything monitors it.
/// </summary>
internal sealed class TenantOptionsCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions> : IOptionsMonitorCache<TOptions>, ITenantOptionsCache, IDisposable
    where TOptions : class
{
    private readonly ConcurrentDictionary<(string? Tenant, string Name), Lazy<TOptions>> _values = new();
    private readonly ICurrentTenantId _tenant;
    private readonly List<IDisposable> _changeSubscriptions = [];

    public TenantOptionsCache(
        ICurrentTenantId tenant, TenantOptionsCaches caches, IEnumerable<IOptionsChangeTokenSource<TOptions>> sources)
    {
        _tenant = tenant;
        caches.Add(this);

        foreach (var source in sources)
        {
            var name = source.Name ?? Microsoft.Extensions.Options.Options.DefaultName;
            _changeSubscriptions.Add(ChangeToken.OnChange(source.GetChangeToken, () => TryRemove(name)));
        }
    }

    public TOptions GetOrAdd(string? name, Func<TOptions> createOptions)
    {
        ArgumentNullException.ThrowIfNull(createOptions);

        return _values.GetOrAdd(Key(name), _ => new Lazy<TOptions>(createOptions)).Value;
    }

    public bool TryAdd(string? name, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _values.TryAdd(Key(name), new Lazy<TOptions>(options));
    }

    // A name's value is removed for every tenant: the configuration it is bound to changed, or it was set again.
    public bool TryRemove(string? name)
    {
        name ??= Microsoft.Extensions.Options.Options.DefaultName;
        var removed = false;

        foreach (var key in _values.Keys.Where(key => key.Name == name))
            removed |= _values.TryRemove(key, out _);

        return removed;
    }

    public void Clear() => _values.Clear();

    public void Remove(string tenantId)
    {
        foreach (var key in _values.Keys.Where(key => key.Tenant == tenantId))
            _values.TryRemove(key, out _);
    }

    public void Dispose()
    {
        foreach (var subscription in _changeSubscriptions)
            subscription.Dispose();
    }

    private (string? Tenant, string Name) Key(string? name) =>
        (_tenant.Current, name ?? Microsoft.Extensions.Options.Options.DefaultName);
}

/// <summary>
/// The <see cref="IOptions{TOptions}"/> and <see cref="IOptionsSnapshot{TOptions}"/> of an options type configured per
/// tenant: each read gives the current tenant's value, from <see cref="TenantOptionsCache{TOptions}"/>, so a singleton
/// that holds it reads the tenant of the code that calls it.
/// </summary>
internal sealed class TenantOptionsManager<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(IOptionsFactory<TOptions> factory, TenantOptionsCache<TOptions> cache)
    : IOptionsSnapshot<TOptions>
    where TOptions : class
{
    public TOptions Value => Get(Microsoft.Extensions.Options.Options.DefaultName);

    public TOptions Get(string? name)
    {
        name ??= Microsoft.Extensions.Options.Options.DefaultName;
        return cache.GetOrAdd(name, () => factory.Create(name));
    }
}

/// <summary>Clears every per-tenant options value of a tenant when <see cref="ITenantStoreCache{TKey}"/> invalidates it.</summary>
internal sealed class TenantOptionsInvalidation<TKey>(TenantOptionsCaches caches) : ITenantInvalidationHandler<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Invalidate(TKey tenantId) => caches.Remove(TenantIds.Format(tenantId));

    public void InvalidateAll() => caches.Clear();
}
