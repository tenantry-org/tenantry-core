namespace Tenantry;

/// <summary>
/// Marks an entity that belongs to a tenant. Tenantry's EF Core integration filters its reads to the current
/// tenant, stamps new ones with it, and rejects writes to another tenant's.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must match the <c>TKey</c> used in the rest of
/// the Tenantry registration (e.g. <see cref="System.Guid"/>, <see langword="string"/>).
/// </typeparam>
/// <remarks>
/// Only a getter is required: the EF Core integration sets <c>TenantId</c> through EF Core's own property access,
/// so the entity may give it a private or init-only setter, or none with a backing field. Derive from
/// <see cref="TenantEntity{TKey}"/> to get the property with a public setter.
/// </remarks>
public interface ITenantEntity<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// The identifier of the tenant that owns this entity. Set on new entities when they are saved.
    /// </summary>
    TKey TenantId { get; }
}
