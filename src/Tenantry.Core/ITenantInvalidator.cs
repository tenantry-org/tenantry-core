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
/// in a cache it shares: other instances keep their own copies until they expire or are invalidated there.
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
    /// <summary>Clears what Tenantry keeps for the tenant <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The id of the tenant that changed.</param>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tenantId"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tenantId"/> is one Tenantry reserves for "no tenant": the key type's default (<c>Guid.Empty</c>,
    /// <c>0</c>) or an empty string.
    /// </exception>
    /// <exception cref="Exception">A handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    ValueTask InvalidateAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>Clears what Tenantry keeps for every tenant.</summary>
    /// <param name="cancellationToken">Cancels the invalidation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <exception cref="Exception">A handler threw (several: <see cref="AggregateException"/>), after every handler ran.</exception>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);
}
