namespace Tenantry;

/// <summary>
/// Opens tenant scopes for work that runs outside an HTTP request: hosted services, queue consumers,
/// scheduled jobs and console tools. Each scope pairs a fresh dependency-injection scope with an active
/// tenant, so scoped services such as a <c>DbContext</c> are created for that tenant and isolated to it.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantDescriptor{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <para>
/// A singleton registered by <c>AddTenantry</c>, used in two ways:
/// </para>
/// <list type="bullet">
/// <item><description>
/// You have the tenant's id (for example from a queue message):
/// <c>await scopes.RunInScopeAsync(tenantId, async (scope, ct) =&gt; { … }, ct);</c>
/// It looks the tenant up and refuses a missing or inactive one.
/// </description></item>
/// <item><description>
/// You already hold the tenant (for example while iterating
/// <see cref="ITenantLookup{TKey}.GetAllTenantsAsync"/>):
/// <c>await using var scope = scopes.CreateScope(tenant);</c>
/// It trusts the descriptor and checks nothing.
/// </description></item>
/// </list>
/// <para>
/// There is no <c>CreateScopeAsync(tenantId)</c>, as a scope opened inside an asynchronous lookup would not be
/// current for the code that awaited it (the tenant is held in an <see cref="AsyncLocal{T}"/>).
/// </para>
/// </remarks>
public interface ITenantScopeFactory<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>
    /// Creates a dependency-injection scope and makes <paramref name="tenant"/> the current tenant for the
    /// calling code until the returned scope is disposed. Services resolved from
    /// <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope.ServiceProvider"/> are created in the
    /// new scope and see this tenant.
    /// </summary>
    /// <param name="tenant">The tenant to activate.</param>
    /// <remarks>
    /// <para>
    /// The tenant is not looked up in the store or checked with <see cref="ITenantActivity{TKey}"/>: the descriptor
    /// becomes current as it is, even when the store does not hold its id, the tenant is inactive, or its other fields
    /// differ from the store's, and shared-database queries and saves use its id, so a descriptor the store does not
    /// hold leaves rows owned by an id the store does not know. Per-tenant options from Tenantry.Options are the
    /// exception: they are built from the store's copy when the store holds the id.
    /// </para>
    /// <para>
    /// Pass a tenant you already hold: one read from <see cref="ITenantLookup{TKey}"/> while iterating the store, or
    /// one being onboarded before its store row exists. Reaching inactive tenants suits provisioning and migrations;
    /// for other work, check <see cref="ITenantActivity{TKey}"/> first. For an id from outside the application, such
    /// as a queue message or a command-line argument, use <see cref="RunInScopeAsync"/>, which looks the tenant up and
    /// refuses a missing or inactive one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The tenant's id is the key type's default value or an empty string, which Tenantry reserves for "no tenant".</exception>
    /// <returns>
    /// The scope. Dispose it (<c>using</c> or <c>await using</c>) to dispose its services and restore the
    /// tenant that was current before it was created.
    /// </returns>
    ITenantScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant);

    /// <summary>
    /// Looks the tenant up with <see cref="ITenantLookup{TKey}"/>, then runs
    /// <paramref name="work"/> inside a new scope for it (see <see cref="CreateScope"/>). The scope is
    /// disposed when the work completes or throws. The caller's current tenant is never changed.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to run the work as.</param>
    /// <param name="work">The work to run. It receives the scope and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Passed to the tenant lookup and to <paramref name="work"/>.</param>
    /// <exception cref="TenantNotFoundException">The tenant store has no tenant with that id.</exception>
    /// <exception cref="TenantInactiveException">An <see cref="ITenantActivityValidator{TKey}"/> refused the tenant.</exception>
    /// <exception cref="InvalidOperationException">No tenant store is registered.</exception>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is the key type's default value or an empty string, which no tenant can have.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the work started.</exception>
    Task RunInScopeAsync(
        TKey tenantId,
        Func<ITenantScope<TKey>, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="RunInScopeAsync(TKey, Func{ITenantScope{TKey}, CancellationToken, Task}, CancellationToken)"/>
    /// <typeparam name="TResult">The type of the work's result.</typeparam>
    /// <returns>The value returned by <paramref name="work"/>.</returns>
    Task<TResult> RunInScopeAsync<TResult>(
        TKey tenantId,
        Func<ITenantScope<TKey>, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);
}
