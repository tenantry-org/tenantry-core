using System.Collections.Concurrent;

namespace Tenantry.Internal;

/// <summary>
/// The cache <c>CacheTenants</c> puts in front of the tenant store: <see cref="ITenantLookup{TKey}"/>, and
/// so the request middleware, read tenants through it. It keeps tenants the store found, by the id or identifier
/// they were looked up with, for <see cref="TenantStoreCacheOptions.Duration"/>; a lookup that finds no tenant is
/// not cached, so a new tenant is found at once.
/// </summary>
internal sealed class TenantStoreCache<TKey> : ITenantStoreCache<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    // Each map stops growing at this size: identifiers are request input, and a store that matches them without
    // regard to case would otherwise cache every variant a client sends.
    internal const int MaxEntries = 100_000;

    private readonly Map<TKey> _byId = new(null);
    private readonly Map<string> _byIdentifier = new(StringComparer.Ordinal);
    private readonly TenantStoreCacheOptions _options;
    private readonly TimeProvider _time;
    private readonly TenantInvalidationHandlers<TKey> _handlers;
    private long _generation;

    public TenantStoreCache(TenantStoreCacheOptions options, TimeProvider time, TenantInvalidationHandlers<TKey> handlers)
    {
        _options = options;
        _time = time;
        _handlers = handlers;
    }

    /// <summary>Changes whenever entries are invalidated; read it before a store lookup and pass it to Set.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    public bool TryGet(TKey tenantId, out ITenantDescriptor<TKey> tenant) => _byId.TryGet(tenantId, _time.GetUtcNow(), out tenant);

    public bool TryGetByIdentifier(string identifier, out ITenantDescriptor<TKey> tenant) =>
        _byIdentifier.TryGet(identifier, _time.GetUtcNow(), out tenant);

    public void Set(TKey tenantId, ITenantDescriptor<TKey> tenant, long generation) => Set(_byId, tenantId, tenant, generation);

    public void SetByIdentifier(string identifier, ITenantDescriptor<TKey> tenant, long generation) =>
        Set(_byIdentifier, identifier, tenant, generation);

    public void Invalidate(TKey tenantId)
    {
        TenantInvalidationHandlers<TKey>.ThrowIfReserved(tenantId);
        Remove(tenantId);
        _handlers.Invalidate(tenantId);
    }

    public void InvalidateAll()
    {
        RemoveAll();
        _handlers.InvalidateAll();
    }

    /// <summary>Removes the tenant's cached copies, by its id and every identifier, without running the handlers.</summary>
    public void Remove(TKey tenantId)
    {
        Interlocked.Increment(ref _generation);
        _byId.RemoveWhere((key, entry) => key.Equals(tenantId) || entry.Tenant.TenantId.Equals(tenantId));
        _byIdentifier.RemoveWhere((_, entry) => entry.Tenant.TenantId.Equals(tenantId));
    }

    /// <summary>Removes every cached tenant, without running the handlers.</summary>
    public void RemoveAll()
    {
        Interlocked.Increment(ref _generation);
        _byId.Entries.Clear();
        _byIdentifier.Entries.Clear();
    }

    private void Set<TLookup>(Map<TLookup> map, TLookup key, ITenantDescriptor<TKey> tenant, long generation)
        where TLookup : notnull
    {
        var now = _time.GetUtcNow();

        if (!map.MakeRoom(now))
        {
            return;
        }

        var duration = _options.Duration;

        // A duration too long to add to the current time (TimeSpan.MaxValue, say) never expires.
        Entry entry = new(tenant, duration < DateTimeOffset.MaxValue - now ? now + duration : DateTimeOffset.MaxValue);
        map.Entries[key] = entry;

        // Invalidated while the store was read, or since: the store's answer may predate the change, so drop it.
        if (Generation != generation)
        {
            map.Entries.TryRemove(KeyValuePair.Create(key, entry));
        }
    }

    private sealed record Entry(ITenantDescriptor<TKey> Tenant, DateTimeOffset ExpiresAt);

    private sealed class Map<TLookup>(IEqualityComparer<TLookup>? comparer)
        where TLookup : notnull
    {
        private const int MinPruneAt = 1024;

        private int _pruneAt = MinPruneAt;
        private long _prunedAtTicks;

        public ConcurrentDictionary<TLookup, Entry> Entries { get; } = new(comparer);

        public bool TryGet(TLookup key, DateTimeOffset now, out ITenantDescriptor<TKey> tenant)
        {
            if (Entries.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > now)
                {
                    tenant = entry.Tenant;
                    return true;
                }

                Entries.TryRemove(KeyValuePair.Create(key, entry));
            }

            tenant = null!;
            return false;
        }

        // Prunes expired entries as the map doubles, and at most once a second once it is full. Returns false when
        // the map is full of entries that have not expired.
        public bool MakeRoom(DateTimeOffset now)
        {
            var count = Entries.Count;

            if (count >= Volatile.Read(ref _pruneAt) &&
                (count < MaxEntries || now.UtcTicks - Interlocked.Read(ref _prunedAtTicks) >= TimeSpan.TicksPerSecond))
            {
                RemoveWhere((_, entry) => entry.ExpiresAt <= now);
                Interlocked.Exchange(ref _prunedAtTicks, now.UtcTicks);
                count = Entries.Count;
                Volatile.Write(ref _pruneAt, Math.Clamp(count * 2, MinPruneAt, MaxEntries));
            }

            return count < MaxEntries;
        }

        public void RemoveWhere(Func<TLookup, Entry, bool> predicate)
        {
            foreach (var pair in Entries.Where(pair => predicate(pair.Key, pair.Value)))
            {
                Entries.TryRemove(pair);
            }
        }
    }
}

/// <summary>
/// The <see cref="ITenantStoreCache{TKey}"/> of an application that does not cache tenants: there are no tenants to
/// remove, so invalidation code runs whether or not <c>CacheTenants</c> is called, and the invalidation handlers still
/// run.
/// </summary>
internal sealed class NoTenantStoreCache<TKey>(TenantInvalidationHandlers<TKey> handlers) : ITenantStoreCache<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Invalidate(TKey tenantId)
    {
        TenantInvalidationHandlers<TKey>.ThrowIfReserved(tenantId);
        handlers.Invalidate(tenantId);
    }

    public void InvalidateAll() => handlers.InvalidateAll();
}

/// <summary>
/// The <see cref="ITenantInvalidator{TKey}"/>: removes the tenant from <c>CacheTenants</c>' cache, when there is one,
/// then runs every <see cref="ITenantInvalidationHandler{TKey}"/>.
/// </summary>
internal sealed class TenantInvalidator<TKey>(TenantStoreCache<TKey>? cache, TenantInvalidationHandlers<TKey> handlers)
    : ITenantInvalidator<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken = default)
    {
        TenantInvalidationHandlers<TKey>.ThrowIfReserved(tenantId);
        cache?.Remove(tenantId);
        return handlers.InvalidateAsync(tenantId, cancellationToken);
    }

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        cache?.RemoveAll();
        return handlers.InvalidateAllAsync(cancellationToken);
    }
}
