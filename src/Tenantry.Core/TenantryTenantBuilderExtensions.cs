using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Internal;

// Builder extensions live in the builder's registration namespace, so they need no using directive.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tenantry's core features on <see cref="ITenantBuilder{TKey}"/>: the tenant store, its cache and per-tenant
/// connection strings.
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
    /// Caches the tenants Tenantry reads from the tenant store, so a request does not ask the store for its tenant
    /// each time. <see cref="ITenantStoreCache{TKey}"/> then removes a tenant that changes (<c>AddTenantry</c> always
    /// registers it, so code that invalidates runs with caching off too).
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets how long a tenant is cached, or <see langword="null"/> for the default (5 minutes).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The cache serves Tenantry's own lookups: <c>app.UseTenantry()</c>'s, and <see cref="ITenantStoreAccessor{TKey}"/>'s,
    /// which <see cref="ITenantScopeFactory{TKey}.RunInScopeAsync(TKey, Func{ITenantScope{TKey}, CancellationToken, Task}, CancellationToken)"/>
    /// and background work use. It keeps each tenant the store finds, by the id or identifier it was looked up with,
    /// in memory for <see cref="TenantStoreCacheOptions.Duration"/>. A lookup that finds no tenant is not cached, so a
    /// tenant added to the store is found at once; <see cref="ITenantStore{TKey}.GetAllTenantsAsync"/> is never
    /// cached. Code that injects <see cref="ITenantStore{TKey}"/> reads the store itself.
    /// </para>
    /// <para>
    /// A tenant that changes (is suspended, say, which an access validator reads) is served as it was until its entry
    /// expires: call <see cref="ITenantStoreCache{TKey}.Invalidate"/> when you change it. Each instance of the
    /// application has its own cache. Calling it again configures the same options. It reads the time from a
    /// registered <see cref="TimeProvider"/>, if there is one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="TenantStoreCacheOptions.Duration"/> is not positive.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromSubdomain()
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .CacheTenants(o =&gt; o.Duration = TimeSpan.FromMinutes(1)));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> CacheTenants<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<TenantStoreCacheOptions>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var options = services
            .FirstOrDefault(d => d.ServiceType == typeof(TenantStoreCacheOptions) && !d.IsKeyedService)
            ?.ImplementationInstance as TenantStoreCacheOptions;

        if (options is null)
        {
            options = new TenantStoreCacheOptions();
            services.AddSingleton(options);
        }

        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Duration, TimeSpan.Zero, "options.Duration");

        services.TryAddSingleton(sp => new TenantStoreCache<TKey>(
            sp.GetRequiredService<TenantStoreCacheOptions>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.Replace(ServiceDescriptor.Singleton<ITenantStoreCache<TKey>>(sp => sp.GetRequiredService<TenantStoreCache<TKey>>()));

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
