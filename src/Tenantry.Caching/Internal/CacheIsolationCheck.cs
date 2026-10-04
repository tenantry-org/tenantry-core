using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Caching.Internal;

/// <summary>
/// Checks, as the host starts, that the <see cref="HybridCache"/> the application resolves, and each keyed one, is one
/// <c>IsolateCaches()</c> keys by tenant. A registration order that would share entries across tenants stops the
/// application instead.
/// </summary>
/// <remarks>
/// It runs through <c>ValidateOnStart</c>, which every .NET host runs before it starts, so it needs no hosting
/// dependency. A service provider built without a host does not run it.
/// </remarks>
internal sealed class CacheIsolationCheck
{
    public static void Register(IServiceCollection services) =>
        services.AddOptions<CacheIsolationCheck>()
            .Validate<IServiceProvider>((_, provider) =>
            {
                ThrowIfShared(services, provider);
                return true;
            })
            .ValidateOnStart();

    private static void ThrowIfShared(IServiceCollection services, IServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        foreach (var key in services.Where(d => d.ServiceType == typeof(HybridCache) && d.IsKeyedService).Select(d => d.ServiceKey).Distinct())
        {
            if (Equals(key, KeyedService.AnyKey))
            {
                throw new InvalidOperationException(
                    "A HybridCache is registered for any key (KeyedService.AnyKey). IsolateCaches() cannot know its keys to " +
                    "clear a tenant's entries from it, so its entries would outlive the tenant's invalidation. Register " +
                    "each keyed HybridCache under its own key, before AddTenantry.");
            }

            if (scope.ServiceProvider.GetKeyedService<HybridCache>(key) is not TenantHybridCache)
            {
                throw new InvalidOperationException(
                    $"The HybridCache with the key '{key}' was registered after AddTenantry, so IsolateCaches() did not key " +
                    "it by tenant and its entries would be shared across tenants. Register it before AddTenantry.");
            }
        }

        switch (scope.ServiceProvider.GetService<HybridCache>())
        {
            case TenantHybridCache:
                return;

            // No HybridCache was registered: fine unless AddHybridCache() ran after AddTenantry, when the application
            // means to use one.
            case MissingHybridCache when !services.Any(IsHybridCacheService):
                return;

            case MissingHybridCache:
                throw new InvalidOperationException(
                    "AddHybridCache() was called after AddTenantry, so IsolateCaches() found no HybridCache to isolate " +
                    "and HybridCache is not available. Register it first: builder.Services.AddHybridCache(), then " +
                    "builder.Services.AddTenantry(...).");

            case var other:
                throw new InvalidOperationException(
                    $"The HybridCache the application resolves is {other?.GetType().FullName ?? "null"}, registered after " +
                    "AddTenantry, so it replaced the one IsolateCaches() keys by tenant and its entries would be shared " +
                    "across tenants. Register the HybridCache before AddTenantry.");
        }
    }

    // AddHybridCache() registers its serializers with the cache, in HybridCache's namespace.
    private static bool IsHybridCacheService(ServiceDescriptor descriptor) =>
        descriptor.ServiceType != typeof(HybridCache) &&
        descriptor.ServiceType.Namespace == typeof(HybridCache).Namespace;
}
