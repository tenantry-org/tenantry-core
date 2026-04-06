namespace Tenantry.Core.Stores;

/// <summary>
/// An <see cref="ITenantStore{TKey}"/> backed by an in-memory dictionary.
/// Suitable for testing, development, demos, and simple single-instance deployments
/// where tenants do not change at runtime.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
/// </typeparam>
public sealed class InMemoryTenantStore<TKey> : ITenantStore<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IReadOnlyDictionary<TKey, ITenantDescriptor<TKey>> _tenants;

    /// <summary>
    /// Initialises the store with a pre-populated collection of tenants.
    /// </summary>
    public InMemoryTenantStore(IEnumerable<ITenantDescriptor<TKey>> tenants)
    {
        _tenants = tenants.ToDictionary(t => t.TenantId, EqualityComparer<TKey>.Default);
    }

    /// <inheritdoc />
    public ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_tenants.GetValueOrDefault(tenantId));

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<TKey>>>([.. _tenants.Values]);
}
