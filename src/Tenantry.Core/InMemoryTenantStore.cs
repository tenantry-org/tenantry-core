namespace Tenantry;

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
    /// <param name="tenants">The tenants the store holds. The store does not change after it is created.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tenants"/> or one of its tenants is null.</exception>
    /// <exception cref="ArgumentException">
    /// A tenant has an id Tenantry reserves for "no tenant" (<see cref="TenantIds.IsReserved{TKey}"/>), two tenants
    /// have the same id, or, with <c>string</c> ids, two ids differ only in case.
    /// </exception>
    /// <remarks>
    /// The store finds a tenant by its exact id. It refuses <c>string</c> ids that differ only in case
    /// because a database whose collation ignores case, the default on SQL Server and MySQL, takes them for one id, so
    /// one tenant's query filter would match the other's rows. It does not refuse ids that differ only in accents or
    /// trailing spaces, which some collations also take for one id: avoid them too.
    /// </remarks>
    public InMemoryTenantStore(IEnumerable<ITenantDescriptor<TKey>> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        Dictionary<TKey, ITenantDescriptor<TKey>> byId = new(EqualityComparer<TKey>.Default);
        Dictionary<string, ITenantDescriptor<TKey>>? byIdIgnoringCase = typeof(TKey) == typeof(string) ? new(StringComparer.OrdinalIgnoreCase) : null;

        foreach (var tenant in tenants)
        {
            if (tenant is null)
            {
                throw new ArgumentNullException(nameof(tenants), "The tenants include null.");
            }

            TenantIds.ThrowIfReserved(tenant, nameof(tenants));

            if (!byId.TryAdd(tenant.TenantId, tenant))
            {
                throw new ArgumentException(
                    $"Tenants '{byId[tenant.TenantId].Name}' and '{tenant.Name}' have the same id " +
                    $"'{TenantIds.Format(tenant.TenantId)}'. Give each tenant an id of its own.",
                    nameof(tenants));
            }

            if (byIdIgnoringCase is not null && tenant.TenantId is string id && !byIdIgnoringCase.TryAdd(id, tenant))
            {
                var other = byIdIgnoringCase[id];
                throw new ArgumentException(
                    $"Tenants '{other.Name}' and '{tenant.Name}' have the ids '{other.TenantId}' and '{id}', which differ " +
                    "only in case. A database whose collation ignores case (the default on SQL Server and MySQL) takes them " +
                    "for one id, so each tenant's query filter would match the other's rows. Give each tenant an id that " +
                    "differs from every other in more than case.",
                    nameof(tenants));
            }
        }

        _tenants = byId;
    }

    /// <inheritdoc />
    public ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_tenants.GetValueOrDefault(tenantId));

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<TKey>>>([.. _tenants.Values]);
}
