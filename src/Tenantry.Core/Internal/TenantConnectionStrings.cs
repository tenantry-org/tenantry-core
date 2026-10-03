using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.Internal;

/// <summary>
/// The decorators <c>DecorateConnectionStrings</c> added, in the order they wrap the base provider.
/// </summary>
internal sealed class TenantConnectionStringDecorators<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public List<Func<IServiceProvider, ITenantConnectionStringProvider<TKey>, ITenantConnectionStringProvider<TKey>>> All { get; } = [];
}

/// <summary>
/// How <see cref="ITenantConnectionStringProvider{TKey}"/> is registered: the base provider (the one
/// <c>UseConnectionStrings</c> configures) under a key of Tenantry's own, and the service itself as that provider
/// wrapped by every decorator, so a decorator applies whichever is called first.
/// </summary>
internal static class TenantConnectionStrings
{
    public const string BaseKey = "Tenantry.ConnectionStrings.Base";

    public static TenantConnectionStringDecorators<TKey> Register<TKey>(IServiceCollection services)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(TenantConnectionStringDecorators<TKey>) && !d.IsKeyedService)
                ?.ImplementationInstance is TenantConnectionStringDecorators<TKey> registered)
        {
            return registered;
        }

        TenantConnectionStringDecorators<TKey> decorators = new();
        services.AddSingleton(decorators);

        // A provider registered before, by the application or a package, becomes the base, keeping its lifetime.
        var lifetime = ServiceLifetime.Singleton;
        var earlier = services.LastOrDefault(d => d.ServiceType == typeof(ITenantConnectionStringProvider<TKey>) && !d.IsKeyedService);

        if (earlier is not null)
        {
            services.RemoveAll<ITenantConnectionStringProvider<TKey>>();
            services.Add(Keyed(earlier));
            lifetime = earlier.Lifetime;
        }

        services.Add(ServiceDescriptor.Describe(
            typeof(ITenantConnectionStringProvider<TKey>),
            sp =>
            {
                var provider = sp.GetKeyedService<ITenantConnectionStringProvider<TKey>>(BaseKey)
                    ?? throw new InvalidOperationException(
                        $"DecorateConnectionStrings wraps the tenants' connection strings, but none are configured: call " +
                        $"UseConnectionStrings in the same AddTenantry.");

                foreach (var decorate in decorators.All)
                {
                    provider = decorate(sp, provider);
                }

                return provider;
            },
            lifetime));

        return decorators;
    }

    /// <summary>Makes <paramref name="factory"/> the base provider, replacing one set before when <paramref name="replace"/> is true.</summary>
    public static void SetBase<TKey>(
        IServiceCollection services,
        Func<IServiceProvider, ITenantConnectionStringProvider<TKey>> factory,
        bool replace)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        Register<TKey>(services);

        var existing = services.Where(d => d.ServiceType == typeof(ITenantConnectionStringProvider<TKey>) && Equals(d.ServiceKey, BaseKey)).ToList();

        if (existing.Count > 0 && !replace)
        {
            return;
        }

        foreach (var descriptor in existing)
        {
            services.Remove(descriptor);
        }

        services.AddKeyedSingleton(BaseKey, (sp, _) => factory(sp));
    }

    private static ServiceDescriptor Keyed(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is { } instance)
        {
            return new ServiceDescriptor(descriptor.ServiceType, BaseKey, instance);
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return new ServiceDescriptor(descriptor.ServiceType, BaseKey, (sp, _) => factory(sp), descriptor.Lifetime);
        }

        return new ServiceDescriptor(descriptor.ServiceType, BaseKey, descriptor.ImplementationType!, descriptor.Lifetime);
    }
}
