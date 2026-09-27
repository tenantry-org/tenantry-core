using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core.Exceptions;

namespace Tenantry.Core.Internal;

/// <summary>
/// Default <see cref="ITenantScopeFactory{TKey}"/>.
/// </summary>
internal sealed class TenantScopeFactory<TKey>(
    IServiceScopeFactory serviceScopes,
    ITenantScope<TKey> tenantScope,
    ITenantStoreAccessor<TKey> tenants)
    : ITenantScopeFactory<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <inheritdoc />
    /// <remarks>
    /// Must stay synchronous: the tenant is activated in the caller's own context, which an
    /// <see langword="async"/> method cannot do.
    /// </remarks>
    public ITenantServiceScope<TKey> CreateScope(ITenantDescriptor<TKey> tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var services = serviceScopes.CreateAsyncScope();

        return new TenantServiceScope<TKey>(services, tenantScope.BeginScope(tenant), tenant);
    }

    /// <inheritdoc />
    public Task RunInScopeAsync(
        TKey tenantId,
        Func<ITenantServiceScope<TKey>, CancellationToken, Task> work,
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
        Func<ITenantServiceScope<TKey>, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        if (tenantId is null)
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = await tenants.GetTenantAsync(tenantId, cancellationToken)
                     ?? throw new TenantNotResolvedException(
                         $"Tenant '{tenantId}' was not found in the tenant store.");

        // The scope is opened inside this method, so it is active for the work and never for the caller.
        await using var scope = CreateScope(tenant);

        return await work(scope, cancellationToken);
    }
}
