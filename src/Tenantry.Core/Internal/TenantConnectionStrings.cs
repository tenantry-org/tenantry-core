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

    /// <summary>The registration of the decorated provider, the service itself.</summary>
    public ServiceDescriptor? Descriptor { get; set; }
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

        decorators.Descriptor = Decorated(decorators, lifetime);
        services.Add(decorators.Descriptor);

        return decorators;
    }

    private static ServiceDescriptor Decorated<TKey>(TenantConnectionStringDecorators<TKey> decorators, ServiceLifetime lifetime)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        ServiceDescriptor.Describe(
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
            lifetime);

    /// <summary>
    /// The base provider registered so far: <see langword="null"/> when there is none, otherwise whether it is the one
    /// <c>UseConnectionStrings(options =&gt; …)</c> builds from its delegates.
    /// </summary>
    public static bool? BaseIsFromDelegates<TKey>(IServiceCollection services)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        Register<TKey>(services);

        var existing = services.LastOrDefault(IsBase<TKey>);

        return existing is null ? null : existing.KeyedImplementationType == typeof(DelegateConnectionStringProvider<TKey>);
    }

    /// <summary>Makes <paramref name="descriptor"/>, a keyed singleton, the base provider, replacing one set before.</summary>
    public static void SetBase<TKey>(IServiceCollection services, ServiceDescriptor descriptor)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var decorators = Register<TKey>(services);

        foreach (var existing in services.Where(IsBase<TKey>).ToList())
        {
            services.Remove(existing);
        }

        services.Add(descriptor);

        // The decorated provider takes the base's lifetime, which an earlier registration may have made scoped.
        if (decorators.Descriptor is { Lifetime: not ServiceLifetime.Singleton } decorated)
        {
            services.Remove(decorated);
            decorators.Descriptor = Decorated(decorators, ServiceLifetime.Singleton);
            services.Add(decorators.Descriptor);
        }
    }

    private static bool IsBase<TKey>(ServiceDescriptor descriptor)
        where TKey : IEquatable<TKey>, IParsable<TKey> =>
        descriptor.ServiceType == typeof(ITenantConnectionStringProvider<TKey>) && Equals(descriptor.ServiceKey, BaseKey);

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
