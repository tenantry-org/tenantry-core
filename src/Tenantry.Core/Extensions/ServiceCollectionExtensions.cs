using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry.Core.Internal;

namespace Tenantry.Core.Extensions;

/// <summary>
/// Extension methods for registering core Tenantry services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Tenantry services: the tenant context accessor (AsyncLocal singleton) behind
    /// <see cref="ITenantContext{TKey}"/> and <see cref="ITenantScope{TKey}"/>, plus the
    /// <see cref="ITenantScopeFactory{TKey}"/> and <see cref="ITenantStoreAccessor{TKey}"/> singletons used
    /// by background work.
    /// </summary>
    /// <remarks>
    /// Use this entry point for worker services, console apps, and other non-HTTP hosts.
    /// For ASP.NET Core applications, use <c>AddTenantry&lt;TKey&gt;()</c> instead,
    /// which calls this method internally and adds HTTP-specific resolution on top.
    ///
    /// All registrations are idempotent — calling both <c>AddTenantryCore</c> and
    /// <c>AddTenantry</c> is safe.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Worker service — no ASP.NET Core required
    /// builder.Services.AddTenantryCore&lt;Guid&gt;(tenant =&gt;
    /// {
    ///     tenant.AddEfCoreIsolation(options =&gt; options.OnMissingTenant = MissingTenantBehavior.Reject);
    /// });
    ///
    /// // Run EF Core work as a tenant, in its own DI scope:
    /// var scopes = sp.GetRequiredService&lt;ITenantScopeFactory&lt;Guid&gt;&gt;();
    /// await using (var scope = scopes.CreateScope(tenant))
    /// {
    ///     var db = scope.ServiceProvider.GetRequiredService&lt;AppDbContext&gt;();
    ///     // reads are filtered to the tenant and writes are stamped with it
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddTenantryCore<TKey>(
        this IServiceCollection services,
        Action<ITenantBuilder<TKey>>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        services.TryAddSingleton<TenantScope<TKey>>();
        services.TryAddSingleton<ITenantContext<TKey>>(sp => sp.GetRequiredService<TenantScope<TKey>>());
        services.TryAddSingleton<ITenantScope<TKey>>(sp => sp.GetRequiredService<TenantScope<TKey>>());
        services.TryAddSingleton<ITenantStoreAccessor<TKey>, TenantStoreAccessor<TKey>>();
        services.TryAddSingleton<ITenantScopeFactory<TKey>, TenantScopeFactory<TKey>>();

        if (configure is not null)
        {
            TenantBuilder<TKey> builder = new(services);
            configure(builder);
        }

        return services;
    }
}
