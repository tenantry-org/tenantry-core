using Microsoft.Extensions.DependencyInjection;
using Tenantry.AspNetCore.Internal;
using Tenantry.Core.Extensions;

namespace Tenantry.AspNetCore.Extensions;

/// <summary>
/// Extension methods for registering TenantKit services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers TenantKit services for ASP.NET Core and returns an <see cref="IAspNetCoreTenantBuilder{TKey}"/>
    /// for further configuration.
    /// </summary>
    /// <typeparam name="TKey">
    /// The tenant identifier type (e.g. <see cref="Guid"/>, <see cref="int"/>, <see cref="string"/>).
    /// Must implement <see cref="IEquatable{T}"/> and <see cref="IParsable{T}"/>.
    /// </typeparam>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =>
    /// {
    ///     tenant.ResolveFromHeader("X-Tenant-Id");
    ///     tenant.UseInMemoryStore(tenants);
    ///     tenant.AddEfCoreIsolation(options => options.StrictIsolation = true);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddTenantry<TKey>(
        this IServiceCollection services,
        Action<IAspNetCoreTenantBuilder<TKey>> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Register core infrastructure (idempotent — safe to call alongside AddTenantry)
        services.AddTenantryCore<TKey>();

        // Register ASP.NET Core specific services
        services.AddSingleton(new TenantResolutionOptions<TKey>());
        services.AddHostedService<TenantConfigurationValidatorHostedService<TKey>>();
        services.AddSingleton<ITenantResolutionMiddlewareConfigurator>(new TenantResolutionMiddlewareConfigurator<TKey>());

        AspNetCoreTenantBuilder<TKey> builder = new(services);
        configure(builder);

        return services;
    }
}
