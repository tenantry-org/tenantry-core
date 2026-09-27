using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.Core.Extensions;

/// <summary>
/// Registers per-tenant connection strings: <see cref="TenantConnectionStringOptions{TKey}"/> and
/// <see cref="ITenantConnectionStringResolver{TKey}"/>.
/// </summary>
public static class ConnectionStringExtensions
{
    /// <summary>
    /// Configures how each tenant's connection string is found and registers
    /// <see cref="ITenantConnectionStringResolver{TKey}"/> as a singleton.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;string&gt;(tenant =&gt;
    /// {
    ///     tenant.ResolveFromHeader("X-Tenant-Id");
    ///     tenant.UseStore&lt;AppTenantStore&gt;();
    ///     tenant.UseConnectionStrings(options =&gt;
    ///         options.GetConnectionString = t =&gt; $"Server=db;Database=app_{t.TenantId};Integrated Security=true");
    /// });
    ///
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt;
    ///     options.UseSqlServer(sp.GetRequiredService&lt;ITenantConnectionStringResolver&lt;string&gt;&gt;().Resolve()));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<TenantConnectionStringOptions<TKey>> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddTenantConnectionStrings(configure);
        return builder;
    }

    /// <summary>
    /// The <see cref="IServiceCollection"/> form of <see cref="UseConnectionStrings{TKey}"/>, for code that
    /// has no <see cref="ITenantBuilder{TKey}"/>. Also registers the core Tenantry services.
    /// </summary>
    /// <remarks>
    /// Calling it again configures the same options instance, so a later call can replace a delegate.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Neither delegate is set after <paramref name="configure"/> runs.</exception>
    public static IServiceCollection AddTenantConnectionStrings<TKey>(
        this IServiceCollection services,
        Action<TenantConnectionStringOptions<TKey>> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = services
            .FirstOrDefault(d => d.ServiceType == typeof(TenantConnectionStringOptions<TKey>) && !d.IsKeyedService)
            ?.ImplementationInstance as TenantConnectionStringOptions<TKey>;

        if (options is null)
        {
            options = new TenantConnectionStringOptions<TKey>();
            services.AddSingleton(options);
        }

        configure(options);

        if (options.GetConnectionString is null && options.GetConnectionStringAsync is null)
        {
            throw new InvalidOperationException(
                "UseConnectionStrings needs GetConnectionString or GetConnectionStringAsync, for example: " +
                "options.GetConnectionString = tenant => $\"...Database=app_{tenant.TenantId}\".");
        }

        services.AddTenantryCore<TKey>();
        services.TryAddSingleton<TenantConnectionStringResolver<TKey>>();
        services.TryAddSingleton<ITenantConnectionStringResolver<TKey>>(sp =>
            sp.GetRequiredService<TenantConnectionStringResolver<TKey>>());

        return services;
    }
}
