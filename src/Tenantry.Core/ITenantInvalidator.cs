namespace Tenantry;

/// <summary>
/// Clears everything Tenantry keeps for a tenant when the tenant changes: its cached copy (with
/// <c>CacheTenants</c>) and what each <see cref="ITenantInvalidationHandler{TKey}"/> keeps, such as Tenantry.Caching's
/// entries, cached responses and Tenantry.Options' values. Call it when a tenant is suspended, renamed or deleted, or
/// its identifiers or settings change.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <c>AddTenantry</c> registers it as a singleton. What it clears is in memory in each instance of the application, or
/// in a cache it shares. To clear the other instances' copies too, register a handler that publishes the invalidation
/// to them with <c>BroadcastInvalidations</c>, and have each instance apply what it receives with
/// <see cref="InvalidateLocallyAsync"/> or <see cref="InvalidateAllLocallyAsync"/>.
/// </remarks>
/// <example>
/// <code>
/// app.MapPost("/admin/tenants/{id}/suspend", async (Guid id, AppDbContext db, ITenantInvalidator&lt;Guid&gt; tenants, CancellationToken ct) =&gt;
/// {
///     await db.Tenants.Where(t =&gt; t.Id == id).ExecuteUpdateAsync(s =&gt; s.SetProperty(t =&gt; t.IsActive, false), ct);
///     await tenants.InvalidateAsync(id, ct);
/// });
/// </code>
/// </example>
public interface ITenantInvalidator<in TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Clears what Tenantry keeps for the tenant <paramref name="tenantId"/> in this instance, then publishes the
    /// invalidation to the other instances through the handlers <c>BroadcastInvalidations</c> registered.
    /// </summary>
    /// <param name="tenantId">The id of the tenant that changed.</param>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <remarks>
    /// Every handler runs even when another throws, the broadcasting ones after the others. An exception from a
    /// broadcasting handler means this instance is invalidated and some others may not be: they keep their copies
    /// until those expire, or until a retry of this call reaches them.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is one Tenantry reserves for "no tenant": the key type's default (<c>Guid.Empty</c>,
    /// <c>0</c>) or an empty string.
    /// </exception>
    /// <exception cref="Exception">A handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears what Tenantry keeps for every tenant in this instance, then publishes the invalidation to the other
    /// instances through the handlers <c>BroadcastInvalidations</c> registered.
    /// </summary>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <remarks>Handlers run, and fail, as for <see cref="InvalidateAsync"/>.</remarks>
    /// <exception cref="Exception">A handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears what Tenantry keeps for the tenant <paramref name="tenantId"/> in this instance only, without publishing
    /// it: for an invalidation another instance published.
    /// </summary>
    /// <param name="tenantId">The id of the tenant that changed.</param>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every handler that is not a broadcasting one has run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is one Tenantry reserves for "no tenant".
    /// </exception>
    /// <exception cref="Exception">A handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    ValueTask InvalidateLocallyAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears what Tenantry keeps for every tenant in this instance only, without publishing it: for an invalidation
    /// another instance published.
    /// </summary>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every handler that is not a broadcasting one has run.</returns>
    /// <exception cref="Exception">A handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    ValueTask InvalidateAllLocallyAsync(CancellationToken cancellationToken = default);
}
