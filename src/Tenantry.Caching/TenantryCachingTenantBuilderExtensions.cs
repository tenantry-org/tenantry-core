using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Caching;
using Tenantry.Caching.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Keeps cached data per tenant.
/// </summary>
public static class TenantryCachingTenantBuilderExtensions
{
    /// <summary>
    /// Keys the application's <see cref="HybridCache"/> by tenant: an entry written while a tenant is current is read
    /// only while that tenant is current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entries every tenant shares go through <see cref="SharedHybridCache"/>; code that uses
    /// <see cref="IDistributedCache"/> directly can inject <see cref="ITenantDistributedCache"/>. Invalidating a tenant
    /// (<see cref="ITenantInvalidator{TKey}.InvalidateAsync"/>) removes its <see cref="HybridCache"/> entries.
    /// </para>
    /// <para>
    /// It wraps the <see cref="HybridCache"/> registered before it, so call <c>AddHybridCache()</c> before
    /// <c>AddTenantry</c>. The host throws <see cref="InvalidOperationException"/> as it starts if a
    /// <see cref="HybridCache"/>, keyed or not, is registered after it (<c>AddHybridCache()</c> included), or one is
    /// registered for any key (<c>KeyedService.AnyKey</c>), whose keys are not known in advance to clear. A service
    /// provider built without a host is not checked. With no <see cref="HybridCache"/> registered at all, the one it
    /// registers throws when used, naming the fix.
    /// </para>
    /// <para>
    /// The cache must be a singleton, as <c>AddHybridCache()</c> registers it: invalidating a tenant clears its entries
    /// outside any scope.
    /// </para>
    /// <para>
    /// A keyed <see cref="HybridCache"/> registered before it is kept per tenant the same way, and the same key gives a
    /// <see cref="SharedHybridCache"/> for that cache's shared entries.
    /// </para>
    /// <para>
    /// A <see cref="HybridCache"/> call with no current tenant throws <see cref="TenantNotResolvedException"/>. Keep
    /// keys within the cache's maximum key length (1,024 characters by default) with the tenant's id added.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">A <see cref="HybridCache"/>, keyed or not, is registered as scoped or transient.</exception>
    /// <example>
    /// <c>AddHybridCache()</c> is in the Microsoft.Extensions.Caching.Hybrid package.
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<TKey>, TenantCacheInvalidation<TKey>>());

        IsolateKeyed(services);

        var registered = services.LastOrDefault(d => d.ServiceType == typeof(HybridCache) && !d.IsKeyedService);

        if (registered is null)
        {
            services.AddSingleton<HybridCache>(new MissingHybridCache());
            return builder;
        }

        ThrowIfNotSingleton(registered);

        // The registered cache becomes the shared one, which the tenants' cache writes through.
        services.Remove(registered);
        services.Add(ServiceDescriptor.Describe(typeof(SharedHybridCache), sp => Create(sp, registered), registered.Lifetime));
        services.Add(ServiceDescriptor.Describe(
            typeof(HybridCache),
            sp => new TenantHybridCache(sp.GetRequiredService<SharedHybridCache>().Inner, sp.GetRequiredService<ICurrentTenant>()),
            registered.Lifetime));

        return builder;
    }

    // Each keyed HybridCache is isolated the same way: [FromKeyedServices(key)] HybridCache keeps entries per tenant,
    // and [FromKeyedServices(key)] SharedHybridCache keeps that cache's shared entries. Invalidation clears each one, so
    // it needs their keys; a registration for any key (KeyedService.AnyKey) has none, and the startup check refuses it.
    private static void IsolateKeyed(IServiceCollection services)
    {
        var keyed = services
            .Where(d => d.ServiceType == typeof(HybridCache) && d.IsKeyedService && !Equals(d.ServiceKey, KeyedService.AnyKey))
            .GroupBy(d => d.ServiceKey)
            .Select(group => group.Last())
            .ToList();

        IsolatedCacheKeys keys = new([.. keyed.Select(d => d.ServiceKey!)]);
        services.AddSingleton(keys);

        foreach (var registered in keyed)
        {
            ThrowIfNotSingleton(registered);

            foreach (var earlier in services.Where(d => d.ServiceType == typeof(HybridCache) && d.IsKeyedService && Equals(d.ServiceKey, registered.ServiceKey)).ToList())
                services.Remove(earlier);

            services.Add(new ServiceDescriptor(
                typeof(SharedHybridCache), registered.ServiceKey, (sp, key) => CreateKeyed(sp, registered, key), registered.Lifetime));
            services.Add(new ServiceDescriptor(
                typeof(HybridCache),
                registered.ServiceKey,
                (sp, key) => new TenantHybridCache(
                    sp.GetRequiredKeyedService<SharedHybridCache>(key).Inner, sp.GetRequiredService<ICurrentTenant>()),
                registered.Lifetime));
        }
    }

    // Invalidating a tenant clears its entries from outside any scope, so the cache has to be the application's one.
    private static void ThrowIfNotSingleton(ServiceDescriptor registered)
    {
        if (registered.Lifetime == ServiceLifetime.Singleton)
            return;

        var which = registered.IsKeyedService ? $"The HybridCache with the key '{registered.ServiceKey}'" : "The HybridCache";

        throw new InvalidOperationException(
            $"{which} is registered as {registered.Lifetime.ToString().ToLowerInvariant()}, and IsolateCaches() needs a " +
            "singleton: invalidating a tenant clears its entries from the application's cache, outside any scope. " +
            "Register it as a singleton, as AddHybridCache() does.");
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

    private static SharedHybridCache CreateKeyed(IServiceProvider services, ServiceDescriptor registered, object? key)
    {
        if (registered.KeyedImplementationInstance is HybridCache instance)
            return new SharedHybridCache(instance, ownsInner: false);

        var created = registered.KeyedImplementationFactory is { } factory
            ? factory(services, key)
            : ActivatorUtilities.CreateInstance(services, registered.KeyedImplementationType!);

        return new SharedHybridCache((HybridCache)created, ownsInner: true);
    }
}
