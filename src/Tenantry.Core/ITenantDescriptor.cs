namespace Tenantry.Core;

/// <summary>
/// Represents a resolved tenant.
/// </summary>
/// <typeparam name="TKey">
/// The type used for tenant identifiers (e.g. <see cref="System.Guid"/>, <see langword="string"/>, <see langword="int"/>).
/// Must implement <see cref="IEquatable{T}"/> so EF Core can translate equality checks to SQL,
/// and <see cref="IParsable{T}"/> so middleware can parse the raw string value from HTTP headers/routes.
/// </typeparam>
public interface ITenantDescriptor<out TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Unique tenant identifier used for data isolation.</summary>
    TKey TenantId { get; }

    /// <summary>Human-readable display name for the tenant.</summary>
    string Name { get; }
}
