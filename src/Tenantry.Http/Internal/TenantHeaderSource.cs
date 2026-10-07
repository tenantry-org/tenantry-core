using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.Http.Internal;

/// <summary>
/// The current tenant's id as <see cref="TenantPropagation.HeaderName"/> carries it, for the handler, which has no
/// key type: <c>UseTenantry()</c> registers it for the application's, read from <see cref="ITenantKeyType"/>.
/// </summary>
internal interface ITenantHeaderSource
{
    /// <summary>The current tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>, or null with no tenant.</summary>
    string? CurrentTenantId { get; }
}

internal sealed class TenantHeaderSource<TKey>(ITenantContext<TKey> tenantContext) : ITenantHeaderSource
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public string? CurrentTenantId => tenantContext.CurrentTenant is { } tenant ? TenantIds.Format(tenant.TenantId) : null;
}

internal static class TenantHeaderSource
{
    /// <summary>Registers the <see cref="ITenantHeaderSource"/> of the key type <c>AddTenantry</c> was called with.</summary>
    public static void Register(IServiceCollection services) =>
        services.TryAddSingleton(Create);

    /// <exception cref="InvalidOperationException">Tenantry is not registered.</exception>
    private static ITenantHeaderSource Create(IServiceProvider services)
    {
        var keyType = services.GetService<ITenantKeyType>() ?? throw new InvalidOperationException(
            "UseTenantry() sends the current tenant with a client's requests, but Tenantry is not registered. " +
            "Register it with builder.Services.AddTenantry<TKey>(...).");

        return keyType.Accept(new Factory(services));
    }

    private sealed class Factory(IServiceProvider services) : ITenantKeyTypeVisitor<ITenantHeaderSource>
    {
        public ITenantHeaderSource Visit<TKey>()
            where TKey : IEquatable<TKey>, IParsable<TKey> =>
            new TenantHeaderSource<TKey>(services.GetRequiredService<ITenantContext<TKey>>());
    }
}
