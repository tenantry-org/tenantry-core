namespace Tenantry;

/// <summary>
/// Returns a tenant's connection string, as configured by <see cref="TenantConnectionStringOptions{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>UseConnectionStrings</c>. It takes the tenant explicitly, for code such as
/// migration runners that visits tenants without making each one current. For the current tenant's connection
/// string, use <see cref="CurrentTenantConnectionString{TKey}"/>.
/// </para>
/// <para>
/// Implement it to decorate the default <see cref="TenantConnectionStringProvider{TKey}"/>, for example to cache.
/// </para>
/// </remarks>
public interface ITenantConnectionStringProvider<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Returns <paramref name="tenant"/>'s connection string.</summary>
    /// <param name="tenant">The tenant whose connection string to return.</param>
    /// <exception cref="InvalidOperationException">
    /// Only <see cref="TenantConnectionStringOptions{TKey}.GetConnectionStringAsync"/> is configured (use
    /// <see cref="GetAsync"/>), or the delegate returned an empty value.
    /// </exception>
    string Get(ITenantDescriptor<TKey> tenant);

    /// <summary>Returns <paramref name="tenant"/>'s connection string, using the asynchronous delegate if configured.</summary>
    /// <param name="tenant">The tenant whose connection string to return.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="InvalidOperationException">The delegate returned an empty value.</exception>
    ValueTask<string> GetAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default);
}
