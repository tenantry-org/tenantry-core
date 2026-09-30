using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.EfCore;
using Tenantry.EfCore.Internal;

// Builder extensions live in the builder's registration namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring EF Core tenant isolation on <see cref="ITenantBuilder{TKey}"/>.
/// </summary>
public static class TenantryEfCoreTenantBuilderExtensions
{
    /// <summary>
    /// Registers EF Core tenant isolation services (the SaveChanges interceptor and the
    /// configured isolation policy). Call this inside your <c>AddTenantry</c> configuration lambda.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the isolation options, such as what happens to a write without a tenant, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>Calling it again configures the same options instance.</remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromHeader("X-Tenant-Id")
    ///     .UseInMemoryStore(tenants)
    ///     .AddEfCoreIsolation(options =&gt; options.OnMissingTenant = MissingTenantBehavior.Warn));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> AddEfCoreIsolation<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<EfCoreIsolationOptions>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The interceptor reads the options at SaveChanges time, so register one instance and configure it in
        // place: calling this again changes the same options rather than being ignored.
        var options = builder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(EfCoreIsolationOptions) && !d.IsKeyedService)
            ?.ImplementationInstance as EfCoreIsolationOptions;

        if (options is null)
        {
            options = new EfCoreIsolationOptions();
            builder.Services.TryAddSingleton(options);
        }

        configure?.Invoke(options);

        builder.Services.TryAddSingleton<TenantSaveChangesInterceptor<TKey>>();
        builder.Services.TryAddSingleton<ITenantInterceptorConfigurator>(new TenantInterceptorConfigurator<TKey>());

        return builder;
    }
}
