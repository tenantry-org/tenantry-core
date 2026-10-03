namespace Tenantry;

/// <summary>
/// Clears what an application or a Tenantry package keeps for each tenant when the tenant changes. Every registered
/// handler runs when <see cref="ITenantStoreCache{TKey}.Invalidate"/> or <see cref="ITenantStoreCache{TKey}.InvalidateAll"/>
/// is called, after the cached tenants are removed, whether or not tenants are cached: Tenantry.Caching's cache entries,
/// Tenantry.AspNetCore's output-cached responses (<c>IsolateOutputCache()</c>) and Tenantry.Options' options register one,
/// so one call clears everything Tenantry keeps for a tenant.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Register a handler as a singleton, once:
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ITenantInvalidationHandler&lt;Guid&gt;, MyHandler&gt;())</c>.
/// The handlers are resolved the first time a tenant is invalidated, so a handler may depend on
/// <see cref="ITenantStoreCache{TKey}"/>. Each runs even when another throws; the exception, or an
/// <see cref="AggregateException"/> of several, is thrown once they have all run.
/// </remarks>
public interface ITenantInvalidationHandler<in TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Clears what is kept for the tenant <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The id of the tenant that changed.</param>
    void Invalidate(TKey tenantId);

    /// <summary>Clears what is kept for every tenant.</summary>
    void InvalidateAll();
}
