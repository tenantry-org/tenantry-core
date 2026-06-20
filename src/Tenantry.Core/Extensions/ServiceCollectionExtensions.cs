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
    /// Registers the core Tenantry services: the tenant context accessor (AsyncLocal singleton)
    /// and the <see cref="ITenantContext{TKey}"/> / <see cref="ITenantScope{TKey}"/> interfaces.
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
    ///     tenant.AddEfCoreIsolation(options =&gt; options.StrictIsolation = true);
    /// });
    ///
    /// // Enter a tenant scope before doing EF Core work:
    /// var scope = sp.GetRequiredService&lt;ITenantScope&lt;Guid&gt;&gt;();
    /// using (scope.BeginScope(new TenantDescriptor&lt;Guid&gt; { TenantId = tenantId, Name = "Acme" }))
    /// {
    ///     // EF Core work runs in the tenant context here
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

        if (configure is not null)
        {
            TenantBuilder<TKey> builder = new(services);
            configure(builder);
        }

        return services;
    }
}
