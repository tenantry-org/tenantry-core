namespace Tenantry;

/// <summary>
/// Makes a tenant current for the calling code, for code that has already found the tenant and needs no new
/// dependency-injection scope. Most code uses <see cref="ITenantScopeFactory{TKey}"/> instead, which also creates
/// a scope for the tenant's services.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// Registered as a singleton by <c>AddTenantry</c>. The current tenant is ambient (held in an
/// <see cref="AsyncLocal{T}"/>): it flows into code the caller awaits or starts, never back to the caller's caller.
/// </remarks>
public interface ITenantContextSetter<TKey> : ITenantContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Makes <paramref name="tenant"/> the current tenant until the returned handle is disposed.
    /// Calls may nest: an inner one shadows the outer tenant, and disposing it restores the outer tenant (the
    /// outermost one restores "no tenant").
    /// </summary>
    /// <param name="tenant">The tenant to make current.</param>
    /// <returns>A handle that restores the previously current tenant on disposal.</returns>
    /// <remarks>
    /// Does not look the tenant up or check that it is active, so pass a tenant you already hold. For an id from
    /// outside the application, use <c>ITenantScopeFactory.RunInScopeAsync</c>, which does both.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The tenant's id is the key type's default value (<see cref="Guid.Empty"/>, <c>0</c>) or an empty string,
    /// which Tenantry reserves for "no tenant".
    /// </exception>
    IDisposable MakeCurrent(ITenantDescriptor<TKey> tenant);

    /// <summary>
    /// Makes no tenant current until the returned handle is disposed, as <see cref="MakeCurrent"/> makes one current:
    /// code inside sees no tenant, and disposing it restores the tenant that was current before.
    /// </summary>
    /// <returns>A handle that restores the previously current tenant on disposal.</returns>
    /// <remarks>
    /// For code that must run as no tenant inside a tenant's flow, such as the rest of a request whose tenant was
    /// refused after it was made current.
    /// </remarks>
    IDisposable MakeNoTenantCurrent();
}
