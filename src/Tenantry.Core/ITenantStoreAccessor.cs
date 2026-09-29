namespace Tenantry.Core;

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
/// application (and fails scope validation in Development). Singletons take this accessor instead, which
/// is correct whatever the store's lifetime. Registered as a singleton by <c>AddTenantryCore</c> and
/// <c>AddTenantry</c>.
/// </remarks>
public interface ITenantStoreAccessor<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Returns the tenant with the given <paramref name="tenantId"/>, or <c>null</c> if the store has none.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to find.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="InvalidOperationException">No <see cref="ITenantStore{TKey}"/> is registered.</exception>
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>Returns all tenants in the store.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="InvalidOperationException">No <see cref="ITenantStore{TKey}"/> is registered.</exception>
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default);
}
