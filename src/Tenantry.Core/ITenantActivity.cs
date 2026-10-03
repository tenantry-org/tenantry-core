namespace Tenantry;

/// <summary>
/// Whether a tenant may have work run for it, as the registered <see cref="ITenantActivityValidator{TKey}"/>s
/// decide. A tenant is active when every validator allows it, and always when none is registered.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>AddTenantry</c>. Tenantry consults it in
/// <see cref="ITenantScopeFactory{TKey}.RunInScopeAsync(TKey, Func{ITenantScope{TKey}, CancellationToken, Task}, CancellationToken)"/>
/// and, in Tenantry.AspNetCore, for each request. Tenantry.Pro's background services, schedulers and message
/// integrations consult it too.
/// </para>
/// <para>
/// <see cref="ITenantScopeFactory{TKey}.CreateScope"/> does not: it takes a tenant you already hold, for work such as
/// provisioning and migrations that must reach suspended tenants. Check this first when you iterate tenants for
/// work of your own.
/// </para>
/// </remarks>
public interface ITenantActivity<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Returns <see langword="true"/> when every validator allows <paramref name="tenant"/>.</summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="TenantInactiveException"/> unless every validator allows <paramref name="tenant"/>.</summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="TenantInactiveException">A validator refused the tenant.</exception>
    ValueTask ThrowIfInactiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default);
}
