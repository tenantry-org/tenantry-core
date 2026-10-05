using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tenantry.Options.Internal;

/// <summary>
/// The current tenant's id as text, for the options caches, which have no key type: <c>ConfigurePerTenant</c>
/// registers it as <see cref="CurrentTenantId{TKey}"/> for the application's tenant key type.
/// </summary>
internal interface ICurrentTenantId
{
    /// <summary>The current tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>, or null with no tenant.</summary>
    string? Current { get; }

    /// <summary>The current tenant's descriptor, or null with no tenant.</summary>
    object? CurrentTenant { get; }

    /// <summary>Makes no tenant current until disposed.</summary>
    IDisposable MakeNoTenantCurrent();

    /// <summary>
    /// Makes the store's copy of the current tenant current until disposed, so a value is built from the tenant as the
    /// store has it, not from the copy the caller holds, which may be older or not the store's at all. Null when there
    /// is no tenant, no store, the store does not hold the id, or the caller already holds the store's copy.
    /// </summary>
    /// <param name="keep">
    /// False when the value must not be kept under the current id. The store does not hold the id: the value is built
    /// from the caller's copy, and keeping it would add an entry for every made-up id and give the next caller with
    /// that id this one's value. Or the store answered with a tenant whose id differs, as from a store that matches ids
    /// without regard to case: the value is built from the store's copy, as request resolution would use it, but not
    /// kept under the caller's id, which no invalidation of the store's id clears.
    /// </param>
    IDisposable? MakeStoreCopyCurrent(out bool keep);
}

internal sealed class CurrentTenantId<TKey>(ITenantContextSetter<TKey> tenantContext, IServiceProvider services)
    : ICurrentTenantId
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private ITenantLookup<TKey>? _lookup;
    private bool _lookupResolved;

    public string? Current => tenantContext.CurrentTenant is { } tenant ? TenantIds.Format(tenant.TenantId) : null;

    public object? CurrentTenant => tenantContext.CurrentTenant;

    public IDisposable MakeNoTenantCurrent() => tenantContext.MakeNoTenantCurrent();

    public IDisposable? MakeStoreCopyCurrent(out bool keep)
    {
        keep = true;

        if (tenantContext.CurrentTenant is not { } tenant || Lookup() is not { } lookup)
            return null;

        // Options have no asynchronous configuration, so the read blocks, once per value built: with CacheTenants it is
        // usually answered from memory. It runs as no tenant, as it does when a request is resolved.
        ITenantDescriptor<TKey>? stored;

        using (tenantContext.MakeNoTenantCurrent())
            stored = ReadBlocking(lookup, tenant.TenantId);

        keep = stored is not null && stored.TenantId.Equals(tenant.TenantId);

        return stored is null || ReferenceEquals(stored, tenant) ? null : tenantContext.MakeCurrent(stored);
    }

    // A store that awaits without ConfigureAwait(false) continues on the caller's synchronization context or task
    // scheduler, which blocking the caller can starve for good. With either in place the read runs on the thread pool,
    // where the ambient tenant still flows; otherwise it runs here, so a read CacheTenants answers costs no thread.
    private static ITenantDescriptor<TKey>? ReadBlocking(ITenantLookup<TKey> lookup, TKey tenantId)
    {
        if (SynchronizationContext.Current is not null || TaskScheduler.Current != TaskScheduler.Default)
            return Task.Run(() => lookup.GetTenantAsync(tenantId).AsTask()).GetAwaiter().GetResult();

        var read = lookup.GetTenantAsync(tenantId);
        return read.IsCompletedSuccessfully ? read.Result : read.AsTask().GetAwaiter().GetResult();
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
/// tenant (and per options name), so <see cref="IOptionsMonitor{TOptions}"/> and Tenantry's
/// <see cref="IOptionsSnapshot{TOptions}"/> give the current tenant's. A change to the configuration a name is bound to
/// clears that name for every tenant, whether or not anything monitors it.
/// </summary>
internal sealed class TenantOptionsCache<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions> : IOptionsMonitorCache<TOptions>, ITenantOptionsCache, IDisposable
    where TOptions : class
{
    private readonly ConcurrentDictionary<(string? Tenant, string Name), Lazy<Built>> _values = new();
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
        var entry = _values.GetOrAdd(key, _ => new Lazy<Built>(() => Create(createOptions)));

        try
        {
            var built = entry.Value;

            if (built.Keep)
                return built.Value;

            // A value that is not kept is the reader's own: one that joined another reader's build builds its own.
            _values.TryRemove(KeyValuePair.Create(key, entry));
            return ReferenceEquals(built.Tenant, _tenant.CurrentTenant) ? built.Value : Create(createOptions).Value;
        }
        catch
        {
            // A Lazy keeps its exception: the failed value is dropped, so the next read builds it again.
            _values.TryRemove(KeyValuePair.Create(key, entry));
            throw;
        }
    }

    /// <summary>The number of values kept, for tests.</summary>
    internal int Count => _values.Count;

    private Built Create(Func<TOptions> createOptions)
    {
        var tenant = _tenant.CurrentTenant;

        using (_tenant.MakeStoreCopyCurrent(out var keep))
            return new Built(createOptions(), keep, tenant);
    }

    public bool TryAdd(string? name, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _values.TryAdd(Key(name), new Lazy<Built>(new Built(options, Keep: true, Tenant: null)));
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

    // Tenant is the descriptor that was current for the reader that built the value.
    private sealed record Built(TOptions Value, bool Keep, object? Tenant);
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
                if (_value is { } built)
                    return built;

                // Not kept when the build throws, so the next read tries again.
                using (tenant.MakeNoTenantCurrent())
                {
                    var created = factory.Create(Microsoft.Extensions.Options.Options.DefaultName);
                    _value = created;
                    return created;
                }
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
