namespace Tenantry;

/// <summary>
/// Provides read-only access to the currently resolved tenant for the active request scope.
/// Registered as a singleton backed by <see cref="System.Threading.AsyncLocal{T}"/> — the
/// value is per-async-context (effectively per HTTP request) rather than per-instance.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
public interface ITenantContext<TKey>
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
    /// The current tenant's identifier, or <c>default(TKey)</c> if no tenant is current: <see langword="null"/>
    /// for reference-type keys such as <see langword="string"/>, but <see cref="Guid.Empty"/> or <c>0</c> for
    /// value-type keys, because <c>TKey?</c> is not nullable for them. Check <see cref="HasTenant"/> to tell "no
    /// tenant" apart; Tenantry never lets a tenant have the default id. Equivalent to
    /// <c>CurrentTenant?.TenantId</c>.
    /// </summary>
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
    /// (<see cref="ITenantContextSetter{TKey}.Use"/>).
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
