namespace Tenantry;

/// <summary>
/// Decides whether a tenant may have work run for it: requests, background work, jobs and messages. Return
/// <see langword="false"/> for a suspended tenant, so suspending it stops all its work.
/// </summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
/// Register one with <c>tenant.ValidateTenantActivity(…)</c>, or as a singleton
/// <see cref="ITenantActivityValidator{TKey}"/>. Every registered validator must allow a tenant.
/// <see cref="ITenantActivity{TKey}"/> asks them. It is a singleton, so a validator must not depend on scoped services;
/// read what it needs from the tenant, which the store returns.
/// </remarks>
public interface ITenantActivityValidator<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Returns <see langword="true"/> when work may run for <paramref name="tenant"/>.</summary>
    /// <param name="tenant">The tenant, as the store returned it.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    ValueTask<bool> IsActiveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken);
}
