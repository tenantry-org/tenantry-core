namespace Tenantry;

/// <summary>
/// Removes cached tenants and runs every <see cref="ITenantInvalidationHandler{TKey}"/>, waiting for them. Call it when a
/// tenant changes or is removed. In asynchronous code, <see cref="ITenantInvalidator{TKey}"/> does the same without
/// blocking.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <c>AddTenantry</c> always registers it, as a singleton. Without <c>tenant.CacheTenants()</c> no tenants are cached,
/// so it has none to remove, but the invalidation handlers still run. A handler that removes entries from a remote cache
/// blocks the calling thread until it is done. The cache is in memory, in each instance of the
/// application: invalidating removes the tenant from this instance's cache, and other instances keep their copy until it
/// expires.
/// </remarks>
public interface ITenantStoreCache<in TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Removes the tenant <paramref name="tenantId"/>, found by its id or by any identifier, from the cache.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is one Tenantry reserves for "no tenant": the key type's default (<c>Guid.Empty</c>,
    /// <c>0</c>) or an empty string.
    /// </exception>
    /// <exception cref="Exception">An invalidation handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    void Invalidate(TKey tenantId);

    /// <summary>Removes every tenant from the cache.</summary>
    /// <exception cref="Exception">An invalidation handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    void InvalidateAll();
}
