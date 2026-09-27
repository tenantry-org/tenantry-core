using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Internal;

/// <summary>
/// Default <see cref="ITenantStoreAccessor{TKey}"/>. Creates a scope for each call and resolves the
/// <see cref="ITenantStore{TKey}"/> from it, so the store's own lifetime is honoured.
/// </summary>
internal sealed class TenantStoreAccessor<TKey>(IServiceScopeFactory serviceScopes) : ITenantStoreAccessor<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <inheritdoc />
    public async ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(
        TKey tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceScopes.CreateAsyncScope();

        return await GetStore(scope).GetTenantAsync(tenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceScopes.CreateAsyncScope();

        return await GetStore(scope).GetAllTenantsAsync(cancellationToken);
    }

    private static ITenantStore<TKey> GetStore(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetService<ITenantStore<TKey>>()
        ?? throw new InvalidOperationException(
            $"No ITenantStore<{typeof(TKey).Name}> is registered. Register one with UseInMemoryStore or " +
            "UseStore when configuring Tenantry.");
}
