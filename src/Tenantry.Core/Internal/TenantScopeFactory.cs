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
    private ITenantLookup<TKey>? _tenants;
    private ITenantActivity<TKey>? _activity;

    // A race resolves the singleton twice, which returns the same instance.
    private ITenantLookup<TKey> Tenants => _tenants ??= services.GetRequiredService<ITenantLookup<TKey>>();

    private ITenantActivity<TKey> Activity => _activity ??= services.GetRequiredService<ITenantActivity<TKey>>();

    /// <inheritdoc />
    /// <remarks>
    /// Must stay synchronous: the tenant is activated in the caller's own context, which an
    /// <see langword="async"/> method cannot do.
    /// </remarks>
    public ITenantScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        // Checked before the services are created, so a tenant Use rejects leaves nothing to dispose.
        TenantIds.ThrowIfUnset(tenant, nameof(tenant));
        var scope = serviceScopes.CreateAsyncScope();

        return new TenantScope<TKey>(scope, tenantContext.Use(tenant), tenant);
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
                await work(scope, ct);
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

        if (TenantIds.IsUnset(tenantId))
        {
            throw new ArgumentException(
                $"'{tenantId}' is the default value of {typeof(TKey).Name}, which Tenantry reserves for \"no tenant\", " +
                "so no tenant has it.",
                nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = await Tenants.GetTenantAsync(tenantId, cancellationToken)
                     ?? throw new TenantNotFoundException(tenantId);

        await Activity.ThrowIfInactiveAsync(tenant, cancellationToken);

        // The scope is opened inside this method, so it is active for the work and never for the caller.
        await using var scope = CreateScope(tenant);

        return await work(scope, cancellationToken);
    }
}
