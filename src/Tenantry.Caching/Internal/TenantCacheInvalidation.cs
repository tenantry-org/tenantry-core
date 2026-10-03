namespace Tenantry.Caching.Internal;

/// <summary>
/// Removes a tenant's <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> entries, by the tenant's tag, when
/// <see cref="ITenantInvalidator{TKey}"/> invalidates the tenant, and every tenant's (but no shared entry) when it
/// invalidates them all.
/// </summary>
internal sealed class TenantCacheInvalidation<TKey>(SharedHybridCache shared) : ITenantInvalidationHandler<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken) =>
        shared.Inner.RemoveByTagAsync(TenantCacheKeys.TenantTag(TenantIds.Format(tenantId)), cancellationToken);

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) =>
        shared.Inner.RemoveByTagAsync(TenantCacheKeys.AllTenantsTag, cancellationToken);
}
