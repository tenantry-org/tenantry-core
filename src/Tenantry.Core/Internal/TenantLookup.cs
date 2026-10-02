using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Internal;

/// <summary>
/// Default <see cref="ITenantLookup{TKey}"/>. Creates a scope for each call and resolves the
/// <see cref="ITenantStore{TKey}"/> from it, so the store's own lifetime is honoured. With <c>CacheTenants</c>, a
/// tenant it finds in the cache needs no scope.
/// </summary>
/// <remarks>
/// It checks when it is created that a store is registered, so a hosted service that depends on it fails as the host
/// starts rather than on its first tenant. The scope factory resolves it on its first lookup.
/// </remarks>
internal sealed class TenantLookup<TKey> : ITenantLookup<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IServiceScopeFactory _serviceScopes;
    private readonly TenantStoreCache<TKey>? _cache;

    public TenantLookup(IServiceScopeFactory serviceScopes, IServiceProvider services)
    {
        // Checked without resolving the store, which may be scoped and create a DbContext.
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(ITenantStore<TKey>)) == false)
        {
            throw new InvalidOperationException(NoStore);
        }

        _serviceScopes = serviceScopes;
        _cache = services.GetService<TenantStoreCache<TKey>>();
    }

    private static string NoStore =>
        $"Tenantry has no tenant store for ITenantStore<{typeof(TKey).Name}>. Register one in AddTenantry, with " +
        "tenant.UseStore<TStore>() or tenant.UseInMemoryStore(...).";

    /// <inheritdoc />
    public async ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(
        TKey tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_cache is null)
        {
            return await Read((store, ct) => store.GetTenantAsync(tenantId, ct), cancellationToken);
        }

        if (_cache.TryGet(tenantId, out var cached))
        {
            return cached;
        }

        var generation = _cache.Generation;
        var tenant = await Read((store, ct) => store.GetTenantAsync(tenantId, ct), cancellationToken);

        if (tenant is not null)
        {
            _cache.Set(tenantId, tenant, generation);
        }

        return tenant;
    }

    /// <inheritdoc />
    public async ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        if (_cache is null)
        {
            return await Read((store, ct) => store.FindByIdentifierAsync(identifier, ct), cancellationToken);
        }

        if (_cache.TryGetByIdentifier(identifier, out var cached))
        {
            return cached;
        }

        var generation = _cache.Generation;
        var tenant = await Read((store, ct) => store.FindByIdentifierAsync(identifier, ct), cancellationToken);

        if (tenant is not null)
        {
            _cache.SetByIdentifier(identifier, tenant, generation);
        }

        return tenant;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default) =>
        Read((store, ct) => store.GetAllTenantsAsync(ct), cancellationToken);

    private async ValueTask<TResult> Read<TResult>(
        Func<ITenantStore<TKey>, CancellationToken, ValueTask<TResult>> read,
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopes.CreateAsyncScope();

        var store = scope.ServiceProvider.GetService<ITenantStore<TKey>>() ?? throw new InvalidOperationException(NoStore);

        return await read(store, cancellationToken);
    }
}
