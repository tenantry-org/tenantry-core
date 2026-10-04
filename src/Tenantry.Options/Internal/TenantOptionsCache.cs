using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>Makes no tenant current until disposed.</summary>
    IDisposable MakeNoTenantCurrent();

    /// <summary>
    /// When the current tenant has been invalidated since the application started, makes the store's copy of it current
    /// until disposed, so a value built now does not come from a copy read before the invalidation. Null otherwise.
    /// </summary>
    IDisposable? MakeLatestCurrent();
}

internal sealed class CurrentTenantId<TKey>(ITenantContextSetter<TKey> tenantContext, TenantOptionsCaches caches, IServiceProvider services)
    : ICurrentTenantId
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private ITenantLookup<TKey>? _lookup;
    private bool _lookupResolved;

    public string? Current => tenantContext.CurrentTenant is { } tenant ? TenantIds.Format(tenant.TenantId) : null;

    public IDisposable MakeNoTenantCurrent() => tenantContext.MakeNoTenantCurrent();

    public IDisposable? MakeLatestCurrent()
    {
        if (tenantContext.CurrentTenant is not { } tenant ||
            !caches.WasInvalidated(TenantIds.Format(tenant.TenantId)) ||
            Lookup() is not { } lookup)
        {
            return null;
        }

        // A request resolved before the invalidation still carries the old copy. Options have no asynchronous
        // configuration, so the read blocks, only when a value is built and the tenant is not in CacheTenants' cache.
        // The store is read as no tenant, as it is when a request is resolved.
        ITenantDescriptor<TKey>? latest;

        using (tenantContext.MakeNoTenantCurrent())
        {
            var read = lookup.GetTenantAsync(tenant.TenantId);
            latest = read.IsCompletedSuccessfully ? read.Result : read.AsTask().GetAwaiter().GetResult();
        }

        // A tenant the store does not have (made current with MakeCurrent, or since removed) keeps the caller's copy.
        return latest is null || ReferenceEquals(latest, tenant) ? null : tenantContext.MakeCurrent(latest);
    }

    private ITenantLookup<TKey>? Lookup()
    {
        if (_lookupResolved)
            return _lookup;

        // Without a store there is nothing newer to read; the lookup's constructor would throw.
        _lookup = services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantStore<TKey>)) == false
            ? null
            : services.GetService<ITenantLookup<TKey>>();
        _lookupResolved = true;
        return _lookup;
    }
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
    private readonly ConcurrentDictionary<string, byte> _invalidated = new();
    private volatile bool _allInvalidated;

    public void Add(ITenantOptionsCache cache) => _caches.Add(cache);

    /// <summary>Whether the tenant's values have been invalidated since the application started, alone or with every tenant's.</summary>
    public bool WasInvalidated(string tenantId) => _allInvalidated || _invalidated.ContainsKey(tenantId);

    public void Remove(string tenantId)
    {
        _invalidated.TryAdd(tenantId, 0);

        foreach (var cache in _caches)
            cache.Remove(tenantId);
    }

    public void Clear()
    {
        _allInvalidated = true;

        foreach (var cache in _caches)
            cache.Clear();
    }
}

/// <summary>
/// The <see cref="IOptionsMonitorCache{TOptions}"/> of an options type configured per tenant: each value is kept per
/// tenant (and per options name), so <see cref="IOptionsMonitor{TOptions}"/> and Tenantry's
/// <see cref="IOptionsSnapshot{TOptions}"/> give the current tenant's. A change to the configuration a name is bound to
/// clears that name for every tenant, whether or not anything monitors it.
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

        // The entry is added before its value is built, so invalidating the tenant during the build removes it.
        var key = Key(name);
        var value = _values.GetOrAdd(key, _ => new Lazy<TOptions>(() => Create(createOptions)));

        try
        {
            return value.Value;
        }
        catch
        {
            // A Lazy keeps its exception: the failed value is dropped, so the next read builds it again.
            _values.TryRemove(KeyValuePair.Create(key, value));
            throw;
        }
    }

    private TOptions Create(Func<TOptions> createOptions)
    {
        using (_tenant.MakeLatestCurrent())
            return createOptions();
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
/// The <see cref="IOptionsSnapshot{TOptions}"/> of an options type configured per tenant: each read gives the current
/// tenant's value, from <see cref="TenantOptionsCache{TOptions}"/>. It is scoped, so scope validation refuses a
/// singleton that would hold it.
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

/// <summary>
/// The <see cref="IOptions{TOptions}"/> of an options type configured per tenant: the ordinary value, built once with no
/// tenant current, as Microsoft's is. A singleton that reads <c>Value</c> in its constructor keeps it for its lifetime,
/// so this value is never a tenant's: the tenant's comes from <see cref="IOptionsSnapshot{TOptions}"/> and
/// <see cref="IOptionsMonitor{TOptions}"/>. The first read while a tenant is current logs a warning (event 3001), since
/// the code that reads it most likely expects the tenant's value.
/// </summary>
internal sealed class TenantFreeOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
    IOptionsFactory<TOptions> factory, ICurrentTenantId tenant, ILogger logger)
    : IOptions<TOptions>
    where TOptions : class
{
    private readonly object _gate = new();
    private volatile TOptions? _value;
    private int _warned;

    public TOptions Value
    {
        get
        {
            if (Volatile.Read(ref _warned) == 0 && tenant.Current is { } tenantId &&
                Interlocked.Exchange(ref _warned, 1) == 0)
            {
                TenantOptionsLog.OrdinaryOptionsReadAsTenant(logger, typeof(TOptions).Name, tenantId);
            }

            if (_value is { } value)
                return value;

            lock (_gate)
            {
                // Not kept when the build throws, so the next read tries again.
                using (tenant.MakeNoTenantCurrent())
                    return _value ??= factory.Create(Microsoft.Extensions.Options.Options.DefaultName);
            }
        }
    }
}

/// <summary>Clears every per-tenant options value of a tenant when <see cref="ITenantInvalidator{TKey}"/> invalidates it.</summary>
internal sealed class TenantOptionsInvalidation<TKey>(TenantOptionsCaches caches) : ITenantInvalidationHandler<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken)
    {
        caches.Remove(TenantIds.Format(tenantId));
        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
    {
        caches.Clear();
        return ValueTask.CompletedTask;
    }
}
