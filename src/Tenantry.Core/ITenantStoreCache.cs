namespace Tenantry;

/// <summary>
/// Removes cached tenants, so the next lookup asks the tenant store again. Use it when a tenant changes (it is
/// suspended, renamed or deleted, or its identifiers change) before its cached copy expires.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <c>AddTenantry</c> always registers it, as a singleton. Without <c>tenant.CacheTenants()</c> nothing is cached, so
/// it has nothing to remove. The cache is in memory, in each instance of the application: invalidating removes the
/// tenant from this instance's cache, and other instances keep their copy until it expires.
/// </remarks>
public interface ITenantStoreCache<in TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Removes the tenant <paramref name="tenantId"/>, found by its id or by any identifier, from the cache.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to remove.</param>
    void Invalidate(TKey tenantId);

    /// <summary>Removes every tenant from the cache.</summary>
    void InvalidateAll();
}
