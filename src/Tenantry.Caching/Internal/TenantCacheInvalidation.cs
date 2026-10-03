using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Caching.Internal;

/// <summary>The keys of the keyed <c>HybridCache</c> registrations <c>IsolateCaches()</c> isolated.</summary>
internal sealed class IsolatedCacheKeys(IReadOnlyList<object> keys)
{
    public IReadOnlyList<object> Keys { get; } = keys;
}

/// <summary>
/// Removes a tenant's <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> entries, by the tenant's tag, from
/// the application's cache and each keyed one, when <see cref="ITenantInvalidator{TKey}"/> invalidates the tenant, and
/// every tenant's (but no shared entry) when it invalidates them all.
/// </summary>
internal sealed class TenantCacheInvalidation<TKey>(IServiceProvider services, IsolatedCacheKeys keys) : ITenantInvalidationHandler<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken) =>
        RemoveAsync(TenantCacheKeys.TenantTag(TenantIds.Format(tenantId)), cancellationToken);

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) =>
        RemoveAsync(TenantCacheKeys.AllTenantsTag, cancellationToken);

    private async ValueTask RemoveAsync(string tag, CancellationToken cancellationToken)
    {
        // No SharedHybridCache when no unkeyed HybridCache was registered.
        if (services.GetService<SharedHybridCache>() is { } shared)
            await shared.Inner.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        foreach (var key in keys.Keys)
            await services.GetRequiredKeyedService<SharedHybridCache>(key).Inner.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
    }
}
