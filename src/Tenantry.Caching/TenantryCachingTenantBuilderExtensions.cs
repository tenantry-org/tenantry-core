using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Caching;
using Tenantry.Caching.Internal;

// Extensions on the Tenantry builder live in the DI namespace, so registration code needs no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Keeps cached data per tenant.
/// </summary>
public static class TenantryCachingTenantBuilderExtensions
{
    /// <summary>
    /// Keys the application's <see cref="HybridCache"/> by tenant: an entry written while a tenant is current is read
    /// only while that tenant is current, so one tenant's cached data is never served to another. Entries every tenant
    /// shares go through <see cref="SharedHybridCache"/>; code that uses <see cref="IDistributedCache"/> directly can
    /// inject <see cref="ITenantDistributedCache"/>. Invalidating a tenant
    /// (<see cref="ITenantInvalidator{TKey}.InvalidateAsync"/>) removes its <see cref="HybridCache"/> entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It wraps the <see cref="HybridCache"/> registered before it, so call <c>AddHybridCache()</c> before
    /// <c>AddTenantry</c>. When the host starts, it checks that <see cref="HybridCache"/> resolves to the cache it keys
    /// by tenant, and throws <see cref="InvalidOperationException"/> otherwise: for a <see cref="HybridCache"/>
    /// registered after it (which would replace it), for <c>AddHybridCache()</c> called after it, and for a keyed
    /// <see cref="HybridCache"/>, which it does not isolate. With no <see cref="HybridCache"/> registered at all, the one
    /// it registers throws when used, naming the fix.
    /// </para>
    /// <para>
    /// A <see cref="HybridCache"/> call with no current tenant throws <see cref="TenantNotResolvedException"/>, rather
    /// than writing an entry no tenant owns. The tenant's prefix makes keys longer: keep them within the cache's
    /// maximum key length (1,024 characters by default) with the id added.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddHybridCache();
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .IsolateCaches());
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> IsolateCaches<TKey>(this ITenantBuilder<TKey> builder)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (services.Any(d => d.ServiceType == typeof(ICurrentTenant)))
            return builder;

        services.AddSingleton<ICurrentTenant>(sp => new CurrentTenant<TKey>(sp.GetRequiredService<ITenantContextSetter<TKey>>()));
        services.TryAddSingleton<ITenantDistributedCache>(sp =>
            new TenantDistributedCache(sp.GetRequiredService<IDistributedCache>(), sp.GetRequiredService<ICurrentTenant>()));
        CacheIsolationCheck.Register(services);

        var registered = services.LastOrDefault(d => d.ServiceType == typeof(HybridCache) && !d.IsKeyedService);

        if (registered is null)
        {
            services.AddSingleton<HybridCache>(new MissingHybridCache());
            return builder;
        }

        // The registered cache becomes the shared one, which the tenants' cache writes through.
        services.Remove(registered);
        services.Add(ServiceDescriptor.Describe(typeof(SharedHybridCache), sp => Create(sp, registered), registered.Lifetime));
        services.Add(ServiceDescriptor.Describe(
            typeof(HybridCache),
            sp => new TenantHybridCache(sp.GetRequiredService<SharedHybridCache>().Inner, sp.GetRequiredService<ICurrentTenant>()),
            registered.Lifetime));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<TKey>, TenantCacheInvalidation<TKey>>());

        return builder;
    }

    private static SharedHybridCache Create(IServiceProvider services, ServiceDescriptor registered)
    {
        if (registered.ImplementationInstance is HybridCache instance)
            return new SharedHybridCache(instance, ownsInner: false);

        var created = registered.ImplementationFactory is { } factory
            ? factory(services)
            : ActivatorUtilities.CreateInstance(services, registered.ImplementationType!);

        return new SharedHybridCache((HybridCache)created, ownsInner: true);
    }
}
