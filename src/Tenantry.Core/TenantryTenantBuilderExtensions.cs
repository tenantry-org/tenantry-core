using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry;
using Tenantry.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Tenantry's core features on <see cref="ITenantBuilder{TKey}"/>: the tenant store, its cache, invalidation across
/// instances, and per-tenant connection strings.
/// </summary>
public static class TenantryTenantBuilderExtensions
{
    /// <summary>
    /// Registers an in-memory tenant store that holds <paramref name="tenants"/>, for tests and single-instance
    /// deployments whose tenants do not change.
    /// </summary>
    /// <remarks>The store is a singleton, built when this is called.</remarks>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="tenants">The tenants the store holds. The store does not change after registration.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">A tenant store is already registered.</exception>
    /// <exception cref="ArgumentException">
    /// A tenant has an id Tenantry reserves for "no tenant" (<see cref="TenantIds.IsReserved{TKey}"/>), two tenants
    /// have the same id, or, with <c>string</c> ids, two ids differ only in case.
    /// </exception>
    public static ITenantBuilder<TKey> UseInMemoryStore<TKey>(
        this ITenantBuilder<TKey> builder,
        IEnumerable<ITenantDescriptor<TKey>> tenants)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tenants);

        TenantStores.ThrowIfRegistered<TKey>(builder.Services);

        // Built now, so a tenant the store refuses fails registration rather than the first request.
        builder.Services.AddSingleton<ITenantStore<TKey>>(new InMemoryTenantStore<TKey>(tenants));
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
    /// The store is scoped and read through <see cref="ITenantLookup{TKey}"/>, which resolves it from a new scope for
    /// each lookup, so the factory may use scoped services such as a <c>DbContext</c>.
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
    /// Stops work for tenants that <paramref name="isActive"/> refuses, such as suspended ones: requests (with
    /// Tenantry.AspNetCore), <c>RunInScopeAsync</c>, and Tenantry.Pro's background work, jobs and messages.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="isActive">Returns <see langword="true"/> when work may run for the tenant.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Calling it again adds another check: a tenant must pass all of them. See <see cref="ITenantActivity{TKey}"/>
    /// for where Tenantry checks.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;string&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .ValidateTenantActivity(t =&gt; t.As&lt;AppTenant&gt;().IsActive));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> ValidateTenantActivity<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<ITenantDescriptor<TKey>, bool> isActive)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(isActive);

        return builder.ValidateTenantActivity((tenant, _) => ValueTask.FromResult(isActive(tenant)));
    }

    /// <inheritdoc cref="ValidateTenantActivity{TKey}(ITenantBuilder{TKey}, Func{ITenantDescriptor{TKey}, bool})"/>
    public static ITenantBuilder<TKey> ValidateTenantActivity<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<bool>> isActive)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(isActive);

        builder.Services.AddSingleton<ITenantActivityValidator<TKey>>(new DelegateTenantActivityValidator<TKey>(isActive));
        return builder;
    }

    /// <summary>
    /// Caches the tenants that Tenantry's own lookups (<c>app.UseTenantry()</c> and <see cref="ITenantLookup{TKey}"/>)
    /// find in the store, for <see cref="TenantStoreCacheOptions.Duration"/> (5 minutes by default).
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets how long a tenant is cached, or <see langword="null"/> for the default.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Lookups that find nothing, and <see cref="ITenantStore{TKey}.GetAllTenantsAsync"/>, are not cached. Call
    /// <see cref="ITenantInvalidator{TKey}.InvalidateAsync"/> when a tenant changes. It uses a registered
    /// <see cref="TimeProvider"/> if there is one.
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
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Duration, TimeSpan.Zero);

        services.TryAddSingleton(sp => new TenantStoreCache<TKey>(
            sp.GetRequiredService<TenantStoreCacheOptions>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        return builder;
    }

    /// <summary>
    /// Adds a handler that publishes each invalidation to the application's other instances, through a message bus or
    /// a pub/sub channel of your own.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="factory">Creates the handler, once, from the application's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The handler is a singleton. <see cref="ITenantInvalidator{TKey}.InvalidateAsync"/> and
    /// <see cref="ITenantInvalidator{TKey}.InvalidateAllAsync"/> run it after every other handler, and
    /// <see cref="ITenantInvalidator{TKey}.InvalidateLocallyAsync"/> and
    /// <see cref="ITenantInvalidator{TKey}.InvalidateAllLocallyAsync"/> do not run it. Each instance applies an
    /// invalidation it receives with the local methods, so it does not publish it again.
    /// </para>
    /// <para>
    /// The handler is called after this instance is invalidated. When it throws, the invalidator throws its exception
    /// once every handler has run: this instance is invalidated, and the others keep their copies until those expire
    /// or a retry reaches them.
    /// </para>
    /// </remarks>
    public static ITenantBuilder<TKey> BroadcastInvalidations<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<IServiceProvider, ITenantInvalidationHandler<TKey>> factory)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddKeyedSingleton(TenantInvalidationHandlers<TKey>.BroadcastKey, (sp, _) => factory(sp));
        return builder;
    }

    /// <summary>
    /// Configures how each tenant's connection string is found.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the delegates that return a tenant's connection string.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="ITenantConnectionStringProvider{TKey}"/> and
    /// <see cref="CurrentTenantConnectionString{TKey}"/> as singletons. Calling it again configures the same options
    /// instance, so a later call can replace a delegate. It cannot be combined with
    /// <c>UseConnectionStrings(sp =&gt; …)</c> or an <see cref="ITenantConnectionStringProvider{TKey}"/> the application
    /// registered before <c>AddTenantry</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Neither delegate is set after <paramref name="configure"/> runs, or another provider is already registered.
    /// </exception>
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

        if (TenantConnectionStrings.BaseIsFromDelegates<TKey>(services) == false)
        {
            throw new InvalidOperationException(
                "UseConnectionStrings(options => …) cannot be combined with UseConnectionStrings(sp => …) or an " +
                $"ITenantConnectionStringProvider<{typeof(TKey).Name}> registered before AddTenantry: use one.");
        }

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

        TenantConnectionStrings.SetBase<TKey>(
            services,
            ServiceDescriptor.KeyedSingleton<ITenantConnectionStringProvider<TKey>, DelegateConnectionStringProvider<TKey>>(
                TenantConnectionStrings.BaseKey));
        services.TryAddSingleton<CurrentTenantConnectionString<TKey>>();

        return builder;
    }

    /// <summary>
    /// Registers the provider that returns each tenant's connection string, built from the application's services,
    /// so it can use a secrets client or other services registered in DI.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="factory">Creates the provider, once, from the application's services.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="ITenantConnectionStringProvider{TKey}"/> and
    /// <see cref="CurrentTenantConnectionString{TKey}"/> as singletons. It replaces a provider set before by this
    /// method or registered by the application before <c>AddTenantry</c>, and cannot be combined with
    /// <c>UseConnectionStrings(options =&gt; …)</c>. A provider that can only read connection strings asynchronously
    /// returns <see langword="false"/> from <see cref="ITenantConnectionStringProvider{TKey}.CanGetSynchronously"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <c>UseConnectionStrings(options =&gt; …)</c> was called before it.
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .UseConnectionStrings(sp =&gt; new VaultConnectionStrings(sp.GetRequiredService&lt;SecretClient&gt;())));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<IServiceProvider, ITenantConnectionStringProvider<TKey>> factory)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        if (TenantConnectionStrings.BaseIsFromDelegates<TKey>(builder.Services) == true)
        {
            throw new InvalidOperationException(
                "UseConnectionStrings(sp => …) cannot be combined with UseConnectionStrings(options => …): use one.");
        }

        TenantConnectionStrings.SetBase<TKey>(
            builder.Services,
            ServiceDescriptor.KeyedSingleton<ITenantConnectionStringProvider<TKey>>(
                TenantConnectionStrings.BaseKey, (sp, _) => factory(sp)));
        builder.Services.TryAddSingleton<CurrentTenantConnectionString<TKey>>();

        return builder;
    }

    /// <summary>
    /// Wraps the registered <see cref="ITenantConnectionStringProvider{TKey}"/>, for example to cache or log. The
    /// decorator applies whether this is called before or after <c>UseConnectionStrings</c>; several decorators
    /// wrap in the order they are added, so the last one added is called first.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="decorate">
    /// Returns the provider to use in place of the one it is given. It runs once, when the provider is first resolved.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// A decorator should forward <see cref="ITenantConnectionStringProvider{TKey}.CanGetSynchronously"/> to the
    /// provider it wraps. Resolving the provider without any connection strings configured throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public static ITenantBuilder<TKey> DecorateConnectionStrings<TKey>(
        this ITenantBuilder<TKey> builder,
        Func<IServiceProvider, ITenantConnectionStringProvider<TKey>, ITenantConnectionStringProvider<TKey>> decorate)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(decorate);

        TenantConnectionStrings.Register<TKey>(builder.Services).All.Add(decorate);

        return builder;
    }

    /// <summary>
    /// Stops Tenantry logging the warnings <paramref name="eventIds"/>, each about configuration the application may
    /// have chosen on purpose. <see cref="TenantryWarnings"/> names the ones it accepts.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="eventIds">The warnings' event ids.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Each call adds to the ids of the others. Ids outside Tenantry's events, 1000 to 2999 and 3001, are kept for the
    /// package that logs them, which reads them with <see cref="TenantryWarnings.IsIgnored"/>. An id applies only to
    /// the package that logs it: 3001 turns off the Tenantry.Options warning, not Tenantry.Pro's event 3001.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An id is one of Tenantry's events that <see cref="TenantryWarnings"/> does not name.
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;string&gt;(tenant =&gt; tenant
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .IgnoreWarnings(TenantryWarnings.StringTenantIdCollation));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> IgnoreWarnings<TKey>(this ITenantBuilder<TKey> builder, params int[] eventIds)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(eventIds);

        TenantryWarnings.Ignore(builder.Services, eventIds);
        return builder;
    }
}
