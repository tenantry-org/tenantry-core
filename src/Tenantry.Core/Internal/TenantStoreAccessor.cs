using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Default <see cref="ITenantStoreAccessor{TKey}"/>. Creates a scope for each call and resolves the
/// <see cref="ITenantStore{TKey}"/> from it, so the store's own lifetime is honoured.
/// </summary>
/// <remarks>
/// It checks when it is created that a store is registered, so a hosted service that depends on it fails as the host
/// starts rather than on its first tenant. The scope factory resolves it on its first lookup.
/// </remarks>
internal sealed class TenantStoreAccessor<TKey> : ITenantStoreAccessor<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IServiceScopeFactory _serviceScopes;

    public TenantStoreAccessor(IServiceScopeFactory serviceScopes, IServiceProvider services)
    {
        // Checked without resolving the store, which may be scoped and create a DbContext.
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantStore<TKey>)) == false)
        {
            throw new InvalidOperationException(NoStore);
        }

        _serviceScopes = serviceScopes;
    }

    private static string NoStore =>
        $"Tenantry has no tenant store for ITenantStore<{typeof(TKey).Name}>. Register one in AddTenantry, with " +
        "tenant.UseStore<TStore>() or tenant.UseInMemoryStore(...).";

    /// <inheritdoc />
    public async ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(
        TKey tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopes.CreateAsyncScope();

        return await GetStore(scope).GetTenantAsync(tenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceScopes.CreateAsyncScope();

        return await GetStore(scope).GetAllTenantsAsync(cancellationToken);
    }

    private static ITenantStore<TKey> GetStore(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetService<ITenantStore<TKey>>() ?? throw new InvalidOperationException(NoStore);
}
