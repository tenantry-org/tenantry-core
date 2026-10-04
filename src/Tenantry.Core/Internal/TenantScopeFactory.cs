using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Default <see cref="ITenantScopeFactory{TKey}"/>.
/// </summary>
/// <remarks>
/// The tenant lookup is resolved on the first <c>RunInScopeAsync</c>, not when the factory is created:
/// <see cref="CreateScope"/> takes a tenant the caller already has, so a host that only creates scopes needs no
/// store.
/// </remarks>
internal sealed class TenantScopeFactory<TKey>(
    IServiceScopeFactory serviceScopes,
    ITenantContextSetter<TKey> tenantContext,
    IServiceProvider services)
    : ITenantScopeFactory<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    // A race resolves the singleton twice, which returns the same instance.
    private ITenantLookup<TKey> Tenants => field ??= services.GetRequiredService<ITenantLookup<TKey>>();

    private ITenantActivity<TKey> Activity => field ??= services.GetRequiredService<ITenantActivity<TKey>>();

    /// <inheritdoc />
    /// <remarks>
    /// Must stay synchronous: the tenant is activated in the caller's own context, which an
    /// <see langword="async"/> method cannot do.
    /// </remarks>
    public ITenantScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        // Checked before the services are created, so a tenant MakeCurrent rejects leaves nothing to dispose.
        TenantIds.ThrowIfReserved(tenant, nameof(tenant));
        var scope = serviceScopes.CreateAsyncScope();

        return new TenantScope<TKey>(scope, tenantContext.MakeCurrent(tenant), tenant);
    }

    /// <inheritdoc />
    public Task RunInScopeAsync(
        TKey tenantId,
        Func<ITenantScope<TKey>, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RunInScopeAsync<object?>(
            tenantId,
            async (scope, ct) =>
            {
                await work(scope, ct).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> RunInScopeAsync<TResult>(
        TKey tenantId,
        Func<ITenantScope<TKey>, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        if (tenantId is null)
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        if (TenantIds.IsReserved(tenantId))
        {
            throw new ArgumentException(
                $"'{tenantId}' is the default value of {typeof(TKey).Name}, which Tenantry reserves for \"no tenant\", " +
                "so no tenant has it.",
                nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();

        // The work and the scope's services are the caller's, so these awaits return to the caller's synchronization
        // context (a desktop app's UI thread): the work starts there, and the services are disposed where they were
        // created, as they would be if the caller ran the work itself.
        var tenant = await Tenants.GetTenantAsync(tenantId, cancellationToken).ConfigureAwait(true)
                     ?? throw new TenantNotFoundException(tenantId);

        await Activity.ThrowIfInactiveAsync(tenant, cancellationToken).ConfigureAwait(true);

        // The scope is opened inside this method, so it is active for the work and never for the caller.
        var scope = CreateScope(tenant);

        await using (scope.ConfigureAwait(true))
        {
            return await work(scope, cancellationToken).ConfigureAwait(true);
        }
    }
}
