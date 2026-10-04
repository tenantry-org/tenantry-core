using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Tenantry.
/// </summary>
public static class TenantryServiceCollectionExtensions
{
    /// <summary>
    /// Registers Tenantry for tenant keys of type <typeparamref name="TKey"/>, then calls
    /// <paramref name="configure"/> to add its features: a tenant store, how requests are resolved to tenants,
    /// connection strings, EF Core isolation and so on.
    /// </summary>
    /// <typeparam name="TKey">
    /// The tenant identifier type (e.g. <see cref="Guid"/>, <see cref="int"/>, <see cref="string"/>).
    /// Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
    /// </typeparam>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">Adds Tenantry's features, or <see langword="null"/> to register only the core services.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The core services are the ambient tenant (<see cref="ITenantContext{TKey}"/> and
    /// <see cref="ITenantContextSetter{TKey}"/>), <see cref="ITenantScopeFactory{TKey}"/>,
    /// <see cref="ITenantLookup{TKey}"/>, <see cref="ITenantInvalidator{TKey}"/>,
    /// <see cref="ITenantActivity{TKey}"/> and <see cref="ITenantKeyType"/>, all singletons. They serve web applications, workers and console
    /// tools alike; the ASP.NET Core features come from the Tenantry.AspNetCore package.
    /// </para>
    /// <para>
    /// Calling it again adds to the same registration, so a library can call it to make sure Tenantry is
    /// registered. An application uses one tenant key type: calling it with another throws.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Tenantry is already registered with another tenant key type.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromHeader("X-Tenant-Id")
    ///     .UseStore&lt;AppTenantStore&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddTenantry<TKey>(
        this IServiceCollection services,
        Action<ITenantBuilder<TKey>>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);

        var registered = services.FindTenantKeyType();

        if (registered is null)
        {
            services.AddSingleton<ITenantKeyType>(new TenantKeyType<TKey>());
        }
        else if (registered.Type != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"Tenantry is already registered with tenant key type '{registered.Type.Name}', so it cannot also " +
                $"use '{typeof(TKey).Name}'. An application uses one tenant key type: call AddTenantry with the same " +
                "type everywhere.");
        }

        services.TryAddSingleton<AmbientTenantContext<TKey>>();
        services.TryAddSingleton<ITenantContext<TKey>>(sp => sp.GetRequiredService<AmbientTenantContext<TKey>>());
        services.TryAddSingleton<ITenantContextSetter<TKey>>(sp => sp.GetRequiredService<AmbientTenantContext<TKey>>());
        services.TryAddSingleton<ITenantLookup<TKey>, TenantLookup<TKey>>();
        services.TryAddSingleton<ITenantScopeFactory<TKey>, TenantScopeFactory<TKey>>();
        services.TryAddSingleton<ITenantActivity<TKey>, TenantActivity<TKey>>();
        services.TryAddSingleton(sp => new TenantInvalidationHandlers<TKey>(sp));
        services.TryAddSingleton<ITenantInvalidator<TKey>>(sp => new TenantInvalidator<TKey>(
            sp.GetService<TenantStoreCache<TKey>>(), sp.GetRequiredService<TenantInvalidationHandlers<TKey>>()));

        configure?.Invoke(new TenantBuilder<TKey>(services));

        return services;
    }

    /// <summary>
    /// Returns the tenant key type Tenantry is registered with in <paramref name="services"/>, or
    /// <see langword="null"/> when <c>AddTenantry</c> has not been called.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <returns>The key type, or <see langword="null"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static ITenantKeyType? FindTenantKeyType(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ITenantKeyType) && !descriptor.IsKeyedService)
            ?.ImplementationInstance as ITenantKeyType;
    }
}
