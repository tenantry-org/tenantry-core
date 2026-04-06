namespace Tenantry.Core;

/// <summary>
/// Provides read-only access to the currently resolved tenant for the active request scope.
/// Registered as a singleton backed by <see cref="System.Threading.AsyncLocal{T}"/> — the
/// value is per-async-context (effectively per HTTP request) rather than per-instance.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
public interface ITenantContext<out TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// The currently resolved tenant, or <c>null</c> if no tenant has been resolved
    /// (e.g. before the middleware has run, or on anonymous endpoints).
    /// </summary>
    ITenantDescriptor<TKey>? CurrentTenant { get; }

    /// <summary>
    /// Returns <c>true</c> if a tenant has been resolved for the current scope.
    /// </summary>
    bool HasTenant { get; }

    /// <summary>
    /// The current tenant's identifier, or <c>null</c> if no tenant is resolved.
    /// Equivalent to <c>CurrentTenant?.TenantId</c> but exposed as a single property for
    /// use in EF Core global query filter expressions — EF Core evaluates single-step
    /// member accesses on the <c>DbContext</c> per-query, avoiding intermediate object caching.
    /// </summary>
    TKey? CurrentTenantId { get; }
}
