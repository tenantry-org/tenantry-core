using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry.Core;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Extensions;

/// <summary>
/// Extension methods for configuring EF Core tenant isolation on <see cref="ITenantBuilder{TKey}"/>.
/// </summary>
public static class TenantBuilderEfCoreExtensions
{
    /// <summary>
    /// Registers EF Core tenant isolation services (the SaveChanges interceptor and the
    /// configured isolation policy). Call this inside your <c>AddTenantry</c> configuration lambda.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the isolation options, such as what happens to a write without a tenant, or <see langword="null"/> for the defaults.</param>
    /// <remarks>Calling it again configures the same options instance.</remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt;
    /// {
    ///     tenant.ResolveFromHeader("X-Tenant-Id");
    ///     tenant.UseInMemoryStore(tenants);
    ///     tenant.AddEfCoreIsolation(options =&gt;
    ///     {
    ///         options.OnMissingTenant = MissingTenantBehavior.Reject;
    ///         options.DetectSpoofedWrites = true;
    ///     });
    /// });
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

        // The spoof validator is cheap and only invoked when DetectSpoofedWrites is enabled.
        builder.Services.TryAddSingleton<StrictIsolationValidator<TKey>>();
        builder.Services.TryAddSingleton<TenantSaveChangesInterceptor<TKey>>();
        builder.Services.TryAddSingleton<ITenantInterceptorConfigurator>(new TenantInterceptorConfigurator<TKey>());

        return builder;
    }
}
