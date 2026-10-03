namespace Tenantry.Caching.Internal;

/// <summary>
/// Removes a tenant's <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> entries, by the tenant's tag, when
/// <see cref="ITenantStoreCache{TKey}"/> invalidates the tenant, and every tenant's (but no shared entry) when it
/// invalidates them all. It waits for the removal: invalidation is rare, and offboarding relies on it being done.
/// </summary>
internal sealed class TenantCacheInvalidation<TKey>(SharedHybridCache shared) : ITenantInvalidationHandler<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Invalidate(TKey tenantId) => Remove(TenantCacheKeys.TenantTag(TenantIds.Format(tenantId)));

    public void InvalidateAll() => Remove(TenantCacheKeys.AllTenantsTag);

    private void Remove(string tag)
    {
        var removal = shared.Inner.RemoveByTagAsync(tag);
        if (!removal.IsCompletedSuccessfully)
            removal.AsTask().GetAwaiter().GetResult();
    }
}
