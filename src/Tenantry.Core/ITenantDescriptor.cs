namespace Tenantry;

/// <summary>
/// A tenant, without its identifier type: the base of <see cref="ITenantDescriptor{TKey}"/>, for code that does not
/// need the tenant's id, such as <see cref="TenantDescriptorExtensions.As{TTenant}"/>.
/// </summary>
public interface ITenantDescriptor
{
    /// <summary>Human-readable display name for the tenant.</summary>
    string Name { get; }
}

/// <summary>
/// Represents a resolved tenant.
/// </summary>
/// <typeparam name="TKey">
/// The type used for tenant identifiers (e.g. <see cref="System.Guid"/>, <see langword="string"/>, <see langword="int"/>).
/// Must implement <see cref="IEquatable{T}"/> so EF Core can translate equality checks to SQL,
/// and <see cref="IParsable{T}"/> so a tenant id can be parsed from text, such as a request's identifier.
/// </typeparam>
/// <remarks>
/// Implement it on your own type to carry what your application knows about a tenant (its plan, region or
/// connection string), and return that type from your <see cref="ITenantStore{TKey}"/>. Tenantry reads only
/// <see cref="TenantId"/> and <see cref="ITenantDescriptor.Name"/>; your code reads the rest with
/// <see cref="TenantDescriptorExtensions.As{TTenant}"/> or <see cref="ITenantContext{TKey}.GetCurrentTenant{TTenant}"/>.
/// </remarks>
public interface ITenantDescriptor<out TKey> : ITenantDescriptor
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Unique tenant identifier used for data isolation.</summary>
    TKey TenantId { get; }
}
