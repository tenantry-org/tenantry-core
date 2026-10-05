namespace Tenantry;

/// <summary>
/// Returns the current tenant's connection string, through the registered
/// <see cref="ITenantConnectionStringProvider{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <param name="tenantContext">Supplies the current tenant.</param>
/// <param name="connectionStrings">Returns a tenant's connection string.</param>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>UseConnectionStrings</c>, for code that opens its own connections. For EF Core,
/// Tenantry.EfCore's <c>AddDbContextPerTenantDatabase</c> connects each context to its tenant's database, pooled or
/// not.
/// </para>
/// <para>
/// Do not read it in the options callback of <c>AddDbContextPool</c>, <c>AddPooledDbContextFactory</c> or
/// <c>AddDbContextFactory</c>, or of <c>AddDbContext</c> with <c>optionsLifetime: ServiceLifetime.Singleton</c>: each
/// keeps its options as a singleton (<c>AddDbContextFactory</c> by default) and runs that callback once, so every
/// context, for every tenant, would keep the first tenant's connection string. Use
/// <c>AddDbContextPerTenantDatabase</c>.
/// </para>
/// </remarks>
public sealed class CurrentTenantConnectionString<TKey>(
    ITenantContext<TKey> tenantContext,
    ITenantConnectionStringProvider<TKey> connectionStrings)
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Returns the current tenant's connection string.</summary>
    /// <exception cref="TenantNotResolvedException">No tenant is current.</exception>
    /// <exception cref="InvalidOperationException">
    /// Only <see cref="TenantConnectionStringOptions{TKey}.GetConnectionStringAsync"/> is configured (use
    /// <see cref="GetAsync"/>), or the delegate returned an empty value.
    /// </exception>
    public string Get() => connectionStrings.Get(CurrentTenant());

    /// <summary>Returns the current tenant's connection string, using the asynchronous delegate if configured.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="TenantNotResolvedException">No tenant is current.</exception>
    /// <exception cref="InvalidOperationException">The delegate returned an empty value.</exception>
    public ValueTask<string> GetAsync(CancellationToken cancellationToken = default) =>
        connectionStrings.GetAsync(CurrentTenant(), cancellationToken);

    private ITenantDescriptor<TKey> CurrentTenant() =>
        tenantContext.CurrentTenant
        ?? throw new TenantNotResolvedException(
            "No tenant is current, so there is no connection string to return. Read it during a request " +
            "(after app.UseTenantry()) or inside a scope from ITenantScopeFactory, or pass the tenant to " +
            "ITenantConnectionStringProvider.");
}
