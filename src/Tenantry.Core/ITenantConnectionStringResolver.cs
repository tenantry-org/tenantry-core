namespace Tenantry.Core;

/// <summary>
/// Returns tenants' connection strings, as configured by <see cref="TenantConnectionStringOptions{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>UseConnectionStrings</c>. The parameterless overloads use the current
/// tenant (from the request, or from a scope opened with <see cref="ITenantScopeFactory{TKey}"/>); the others
/// take the tenant explicitly, for code such as migration runners that visits tenants without making each one
/// current.
/// </para>
/// <para>
/// With a regular <c>AddDbContext</c>, resolve in the options callback, which runs for every new context:
/// <c>options.UseSqlServer(sp.GetRequiredService&lt;ITenantConnectionStringResolver&lt;Guid&gt;&gt;().Resolve())</c>.
/// Do not do this with <c>AddDbContextPool</c> or <c>AddPooledDbContextFactory</c>: their options callback
/// runs once, so every pooled context would keep the first tenant's connection string.
/// </para>
/// </remarks>
public interface ITenantConnectionStringResolver<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Returns the current tenant's connection string.</summary>
    /// <exception cref="Exceptions.TenantNotResolvedException">No tenant is current.</exception>
    /// <exception cref="InvalidOperationException">
    /// Only <see cref="TenantConnectionStringOptions{TKey}.GetConnectionStringAsync"/> is configured (use
    /// <see cref="ResolveAsync(CancellationToken)"/>), or the delegate returned an empty value.
    /// </exception>
    string Resolve();

    /// <summary>Returns the current tenant's connection string, using the asynchronous delegate if configured.</summary>
    /// <exception cref="Exceptions.TenantNotResolvedException">No tenant is current.</exception>
    /// <exception cref="InvalidOperationException">The delegate returned an empty value.</exception>
    ValueTask<string> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns <paramref name="tenant"/>'s connection string.</summary>
    /// <exception cref="InvalidOperationException">
    /// Only <see cref="TenantConnectionStringOptions{TKey}.GetConnectionStringAsync"/> is configured (use
    /// <see cref="ResolveAsync(ITenantDescriptor{TKey}, CancellationToken)"/>), or the delegate returned an
    /// empty value.
    /// </exception>
    string Resolve(ITenantDescriptor<TKey> tenant);

    /// <summary>Returns <paramref name="tenant"/>'s connection string, using the asynchronous delegate if configured.</summary>
    /// <exception cref="InvalidOperationException">The delegate returned an empty value.</exception>
    ValueTask<string> ResolveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default);
}
