namespace Tenantry.Core;

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
/// Registered as a singleton by <c>AddTenantryCore</c> and <c>AddTenantry</c>, so hosted services can
/// take it as a constructor dependency. There are two ways to use it:
/// </para>
/// <list type="bullet">
/// <item><description>
/// You already hold the tenant (for example while iterating
/// <see cref="ITenantStoreAccessor{TKey}.GetAllTenantsAsync"/>):
/// <c>await using var scope = scopes.CreateScope(tenant);</c>
/// </description></item>
/// <item><description>
/// You only have its id (for example from a queue message):
/// <c>await scopes.RunInScopeAsync(tenantId, async (scope, ct) =&gt; { … }, ct);</c>
/// </description></item>
/// </list>
/// <para>
/// There is deliberately no <c>CreateScopeAsync(tenantId)</c>. The tenant is held in an
/// <see cref="AsyncLocal{T}"/>, and changes an <see langword="async"/> method makes to one never reach its
/// caller, so a scope opened inside an asynchronous lookup would not be active for the code that awaited
/// it. <see cref="RunInScopeAsync"/> does the lookup and then runs your work inside the scope instead.
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
    /// <returns>
    /// The scope. Dispose it (<c>using</c> or <c>await using</c>) to dispose its services and restore the
    /// tenant that was current before it was created.
    /// </returns>
    ITenantServiceScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant);

    /// <summary>
    /// Looks the tenant up with <see cref="ITenantStoreAccessor{TKey}"/>, then runs
    /// <paramref name="work"/> inside a new scope for it (see <see cref="CreateScope"/>). The scope is
    /// disposed when the work completes or throws. The caller's current tenant is never changed.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to run the work as.</param>
    /// <param name="work">The work to run. It receives the scope and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Passed to the tenant lookup and to <paramref name="work"/>.</param>
    /// <exception cref="Exceptions.TenantNotResolvedException">The tenant store has no tenant with that id.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the work started.</exception>
    Task RunInScopeAsync(
        TKey tenantId,
        Func<ITenantServiceScope<TKey>, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="RunInScopeAsync(TKey, Func{ITenantServiceScope{TKey}, CancellationToken, Task}, CancellationToken)"/>
    /// <returns>The value returned by <paramref name="work"/>.</returns>
    Task<TResult> RunInScopeAsync<TResult>(
        TKey tenantId,
        Func<ITenantServiceScope<TKey>, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);
}
