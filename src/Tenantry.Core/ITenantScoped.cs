namespace Tenantry.Core;

/// <summary>
/// Marker interface for objects that belong to a specific tenant.
/// Implement this interface on any object that the tenant should isolate.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. Must match the <c>TKey</c> used in the rest of
/// the TenantKit registration (e.g. <see cref="System.Guid"/>, <see langword="string"/>).
/// </typeparam>
/// <remarks>
/// When using the TenantKit EF Core integration, the <c>TenantId</c> property will be
/// automatically set by an interceptor on <c>SaveChanges</c>.
/// No entity is saved to the wrong tenant. No base class is required.
/// </remarks>
public interface ITenantScoped<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// The identifier of the tenant that owns this entity.
    /// This value is automatically set by the interceptor on <c>SaveChanges</c>.
    /// </summary>
    TKey TenantId { get; set; }
}
