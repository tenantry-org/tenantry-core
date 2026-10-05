namespace Tenantry;

/// <summary>
/// Clears data kept per tenant when the tenant changes, or, registered with <c>BroadcastInvalidations</c>, publishes the
/// invalidation to the application's other instances. Every registered handler runs on
/// <see cref="ITenantInvalidator{TKey}"/> invalidation, with or without <c>CacheTenants</c>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Tenantry.Caching, <c>IsolateOutputCache()</c> and Tenantry.Options register their own. Register a handler as a
/// singleton, once, with <c>TryAddEnumerable</c>. When the application also injects the handler
/// to read what it keeps, register it once and forward the handler registration to that instance:
/// <code>
/// services.AddSingleton&lt;PriceListCache&gt;();
/// services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ITenantInvalidationHandler&lt;Guid&gt;, PriceListCache&gt;(
///     sp =&gt; sp.GetRequiredService&lt;PriceListCache&gt;()));
/// </code>
/// The handlers are resolved the first time a tenant is invalidated, so a handler may depend on
/// <see cref="ITenantInvalidator{TKey}"/>. They run one after another, and each runs even when another throws; the
/// exception, or an <see cref="AggregateException"/> of several, is thrown once they have all run. A cancelled token
/// stops them before the next handler, with an <see cref="OperationCanceledException"/> in place of those exceptions.
/// A handler registered with <c>BroadcastInvalidations</c> runs after the others, and only for
/// <see cref="ITenantInvalidator{TKey}.InvalidateAsync"/> and <see cref="ITenantInvalidator{TKey}.InvalidateAllAsync"/>,
/// so an instance that applies a received invalidation with <see cref="ITenantInvalidator{TKey}.InvalidateLocallyAsync"/>
/// does not publish it again.
/// </remarks>
public interface ITenantInvalidationHandler<in TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Clears what is kept for the tenant <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The id of the tenant that changed.</param>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when the tenant's data is cleared.</returns>
    ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken);

    /// <summary>Clears what is kept for every tenant.</summary>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every tenant's data is cleared.</returns>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken);
}
