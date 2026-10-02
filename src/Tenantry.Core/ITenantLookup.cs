namespace Tenantry;

/// <summary>
/// Reads tenants from the registered <see cref="ITenantStore{TKey}"/> on behalf of singletons, such as
/// hosted services, resolving the store from a fresh dependency-injection scope for each call.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// A store registered with <c>UseStore</c> is scoped, and may depend on scoped services such as a
/// <c>DbContext</c>. Injecting it into a singleton would capture one instance for the life of the
/// application (and fails scope validation in Development). Singletons take this lookup instead, which
/// is correct whatever the store's lifetime. Registered as a singleton by <c>AddTenantry</c>. Creating it throws
/// <see cref="InvalidOperationException"/> when no store is registered, so a hosted service that depends on it fails
/// as the host starts.
/// </remarks>
public interface ITenantLookup<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Returns the tenant with the given <paramref name="tenantId"/>, or <c>null</c> if the store has none.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to find.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the tenant an identifier names, or <c>null</c> if it names none, with the store's
    /// <see cref="ITenantStore{TKey}.FindByIdentifierAsync"/>.
    /// </summary>
    /// <param name="identifier">The identifier: the tenant's id, or a name the store maps to a tenant.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>Returns all tenants in the store.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default);
}
