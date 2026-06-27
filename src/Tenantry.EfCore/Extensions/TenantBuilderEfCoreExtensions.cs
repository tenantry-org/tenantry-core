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

        EfCoreIsolationOptions options = new();
        configure?.Invoke(options);

        // The resolved options are read by the interceptor at SaveChanges time, so register the
        // configured instance directly. The spoof validator is cheap and only invoked when
        // DetectSpoofedWrites is enabled.
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<StrictIsolationValidator<TKey>>();
        builder.Services.TryAddSingleton<TenantSaveChangesInterceptor<TKey>>();
        builder.Services.TryAddSingleton<ITenantInterceptorConfigurator>(new TenantInterceptorConfigurator<TKey>());

        return builder;
    }
}
