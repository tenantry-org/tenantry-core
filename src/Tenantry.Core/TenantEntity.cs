namespace Tenantry;

/// <summary>
/// Optional base class for tenant-owned entities, implementing <see cref="ITenantEntity{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantEntity{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// The setter is public for code that must name the tenant itself, such as seeding or maintenance code that
/// saves without a current tenant. Code that runs as a tenant leaves it unset: new entities are stamped with the
/// current tenant when they are saved.
/// </remarks>
public abstract class TenantEntity<TKey> : ITenantEntity<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <inheritdoc />
    public TKey TenantId { get; set; } = default!;
}
