using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Finds the application's services, and Tenantry's among them, from a context's options.
/// </summary>
internal static class ApplicationServices
{
    public static IServiceProvider? Find(IDbContextOptions options) =>
        options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

    public static IServiceProvider? Find(DbContext context) => Find(context.GetService<IDbContextOptions>());

    /// <summary>
    /// The isolation options the context follows: its own, from <c>UseTenantry(configure)</c>, or else the
    /// application's, from <c>ConfigureEfCoreIsolation</c>.
    /// </summary>
    public static EfCoreIsolationOptions Isolation(DbContext context, IServiceProvider? services) =>
        context.GetService<IDbContextOptions>().FindExtension<TenantryOptionsExtension>()?.Isolation
        ?? services?.GetService<IOptions<EfCoreIsolationOptions>>()?.Value
        ?? Defaults;

    private static readonly EfCoreIsolationOptions Defaults = new();

    /// <summary>The current tenant, as the context's application sees it.</summary>
    /// <exception cref="InvalidOperationException">The context has no application service provider, or Tenantry is not registered in it for <typeparamref name="TKey"/>.</exception>
    public static ITenantContext<TKey> TenantContext<TKey>(DbContext context)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        TenantContext<TKey>(
            Find(context) ?? throw new InvalidOperationException(
                $"This '{context.GetType().Name}' has no application service provider, so Tenantry cannot find the " +
                "current tenant. Register the context with AddDbContext, AddDbContextPool, AddDbContextFactory or " +
                "AddPooledDbContextFactory, or call UseApplicationServiceProvider on its options."));

    /// <exception cref="InvalidOperationException">Tenantry is not registered in <paramref name="services"/> for <typeparamref name="TKey"/>.</exception>
    public static ITenantContext<TKey> TenantContext<TKey>(IServiceProvider services)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        services.GetService<ITenantContext<TKey>>()
        ?? throw new InvalidOperationException(
            $"The model's tenant-owned entities implement ITenantEntity<{typeof(TKey).Name}>, but Tenantry is not " +
            $"registered for '{typeof(TKey).Name}' tenant keys. Add it with builder.Services.AddTenantry<{typeof(TKey).Name}>(…), " +
            "using the tenant key type of your entities.");
}
