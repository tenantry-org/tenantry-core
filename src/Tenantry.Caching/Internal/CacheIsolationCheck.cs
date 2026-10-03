using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tenantry.Caching.Internal;

/// <summary>
/// Checks, as the host starts, that the <see cref="HybridCache"/> the application resolves is the one
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

    internal static void ThrowIfShared(IServiceCollection services, IServiceProvider provider)
    {
        var keyed = services
            .Where(d => d.ServiceType == typeof(HybridCache) && d.IsKeyedService)
            .Select(d => $"'{d.ServiceKey}'")
            .ToList();

        if (keyed.Count > 0)
        {
            throw new InvalidOperationException(
                $"IsolateCaches() keys only the HybridCache registered without a key, so the keyed HybridCache " +
                $"{string.Join(", ", keyed)} would share its entries across tenants. Inject HybridCache for each " +
                "tenant's entries and SharedHybridCache for entries every tenant shares, and remove the keyed registration.");
        }

        using var scope = provider.CreateScope();

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
