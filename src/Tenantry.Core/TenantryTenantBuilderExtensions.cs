using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Internal;

// Builder extensions live in the builder's registration namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tenantry's core features on <see cref="ITenantBuilder{TKey}"/>: the tenant store and per-tenant connection
/// strings.
/// </summary>
public static class TenantryTenantBuilderExtensions
{
    /// <summary>
    /// Registers a pre-populated in-memory tenant store.
    /// Suitable for testing and simple single-instance deployments.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="tenants">The tenants the store holds. The store does not change after registration.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">A tenant store is already registered.</exception>
    public static ITenantBuilder<TKey> UseInMemoryStore<TKey>(
        this ITenantBuilder<TKey> builder,
        IEnumerable<ITenantDescriptor<TKey>> tenants)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tenants);

        TenantStores.ThrowIfRegistered<TKey>(builder.Services);
        builder.Services.AddSingleton<ITenantStore<TKey>>(_ => new InMemoryTenantStore<TKey>(tenants));
        return builder;
    }

    /// <summary>
    /// Registers a custom <see cref="ITenantStore{TKey}"/> implementation with a factory function.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="factory">Creates the store from the scope's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// The store is registered with a <strong>scoped</strong> lifetime and is resolved per operation, so
    /// the factory may return an instance that depends on scoped services such as a <c>DbContext</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A tenant store is already registered.</exception>
    public static ITenantBuilder<TKey> UseStore<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<IServiceProvider, ITenantStore<TKey>> factory)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        TenantStores.ThrowIfRegistered<TKey>(builder.Services);
        builder.Services.AddScoped(factory);
        return builder;
    }

    /// <summary>
    /// Configures how each tenant's connection string is found, and registers
    /// <see cref="ITenantConnectionStringProvider{TKey}"/> and <see cref="CurrentTenantConnectionString{TKey}"/>
    /// as singletons.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the delegates that return a tenant's connection string.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Calling it again configures the same options instance, so a later call can replace a delegate.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Neither delegate is set after <paramref name="configure"/> runs.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;string&gt;(tenant =&gt; tenant
    ///     .ResolveFromHeader("X-Tenant-Id")
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .UseConnectionStrings(options =&gt;
    ///         options.GetConnectionString = t =&gt; $"Server=db;Database=app_{t.TenantId};Integrated Security=true"));
    ///
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt;
    ///     options.UseSqlServer(sp.GetRequiredService&lt;CurrentTenantConnectionString&lt;string&gt;&gt;().Get()));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<TenantConnectionStringOptions<TKey>> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;
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

        services.TryAddSingleton<TenantConnectionStringProvider<TKey>>();
        services.TryAddSingleton<ITenantConnectionStringProvider<TKey>>(sp =>
            sp.GetRequiredService<TenantConnectionStringProvider<TKey>>());
        services.TryAddSingleton<CurrentTenantConnectionString<TKey>>();

        return builder;
    }
}
