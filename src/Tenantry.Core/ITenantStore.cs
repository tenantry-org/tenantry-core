namespace Tenantry.Core;

/// <summary>
/// Persists and retrieves tenant definitions.
/// Implement this interface to back tenants with a database, configuration file,
/// or any other store.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
public interface ITenantStore<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Returns the tenant with the given <paramref name="tenantId"/>,
    /// or <c>null</c> if no matching tenant exists.
    /// </summary>
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all registered tenants.
    /// </summary>
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default);
}
