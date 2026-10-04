namespace Tenantry;

/// <summary>
/// The current tenant, for a request or an <see cref="ITenantScopeFactory{TKey}"/> scope.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// A singleton over an <see cref="System.Threading.AsyncLocal{T}"/>: the value belongs to the async flow, not to the
/// instance.
/// </remarks>
public interface ITenantContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// The current tenant, or <c>null</c> when none is current: no tenant was resolved for the request, or the code
    /// runs outside a tenant scope.
    /// </summary>
    ITenantDescriptor<TKey>? CurrentTenant { get; }

    /// <summary>
    /// Returns <c>true</c> if a tenant has been resolved for the current scope.
    /// </summary>
    bool HasTenant { get; }

    /// <summary>
    /// The current tenant's id, or <c>default(TKey)</c> when no tenant is current. Equivalent to
    /// <c>CurrentTenant?.TenantId</c>.
    /// </summary>
    /// <remarks>
    /// For value-type keys that default is <see cref="Guid.Empty"/> or <c>0</c>, not <see langword="null"/>, because
    /// <c>TKey?</c> is not nullable for them, so check <see cref="HasTenant"/>. No tenant can have the default id.
    /// </remarks>
    TKey? CurrentTenantId { get; }

    /// <summary>
    /// The current tenant as <typeparamref name="TTenant"/>, the type your tenant store returns, or
    /// <see langword="null"/> if no tenant is current. Equivalent to <c>CurrentTenant?.As&lt;TTenant&gt;()</c>.
    /// </summary>
    /// <typeparam name="TTenant">Your tenant type.</typeparam>
    /// <remarks>
    /// A default interface method: a mock of <see cref="ITenantContext{TKey}"/> (NSubstitute, Moq) intercepts it and
    /// returns <see langword="null"/> unless it is configured too, so code under test with a mocked context can read
    /// <c>CurrentTenant?.As&lt;TTenant&gt;()</c> instead, or tests can use a real context
    /// (<see cref="ITenantContextSetter{TKey}.MakeCurrent"/>).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The current tenant is not a <typeparamref name="TTenant"/>: the tenant store returns another type.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapGet("/plan", (ITenantContext&lt;Guid&gt; tenants) =&gt; tenants.GetCurrentTenant&lt;AppTenant&gt;()?.Plan);
    /// </code>
    /// </example>
    TTenant? GetCurrentTenant<TTenant>()
        where TTenant : class, ITenantDescriptor<TKey> =>
        CurrentTenant?.As<TTenant>();
}
