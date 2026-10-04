using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
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
    // What EF Core requires of a context type it creates (matches its own annotations).
    internal const DynamicallyAccessedMemberTypes ContextMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties;

    /// <summary>
    /// Sets the EF Core isolation options, such as what happens to a write without a tenant. Optional: without it,
    /// the defaults apply, which are the strictest.
    /// </summary>
    /// <typeparam name="TKey">The tenant identifier type.</typeparam>
    /// <param name="builder">The tenant builder.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// These are the defaults for every context. A context registered with <c>UseTenantry(configure)</c> uses its own
    /// instead, so keep the defaults strict and relax them only on a context for maintenance code. The options are
    /// ordinary <c>IOptions&lt;EfCoreIsolationOptions&gt;</c>, so <c>services.Configure</c> also sets them.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromHeader("X-Tenant-Id")
    ///     .UseInMemoryStore(tenants)
    ///     .ConfigureEfCoreIsolation(options =&gt; options.OnMissingTenant = MissingTenantBehavior.Warn));
    /// </code>
    /// </example>
    public static ITenantBuilder<TKey> ConfigureEfCoreIsolation<TKey>(
        this ITenantBuilder<TKey> builder,
        Action<EfCoreIsolationOptions> configure)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddOptions<EfCoreIsolationOptions>().Configure(configure);

        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> for a database per tenant: each context is connected to the current
    /// tenant's database, through <see cref="ITenantConnectionStringProvider{TKey}"/>, and uses
    /// <c>UseTenantry()</c>. Registers a scoped <typeparamref name="TContext"/> and a singleton
    /// <see cref="IDbContextFactory{TContext}"/>.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="builder">The tenant builder, after <c>UseConnectionStrings</c> (or another registration of <see cref="ITenantConnectionStringProvider{TKey}"/>).</param>
    /// <param name="configure">
    /// Configures the context's options, <em>without</em> a connection string: for example
    /// <c>(sp, options) =&gt; options.UseSqlServer()</c>.
    /// </param>
    /// <param name="pooled">
    /// Whether to reuse context instances from a pool, as <c>AddDbContextPool</c> does. A pooled context needs a
    /// constructor that takes only its options.
    /// </param>
    /// <param name="poolSize">The most contexts the pool keeps for reuse, when <paramref name="pooled"/>.</param>
    /// <returns>The same builder, without its key type: call methods that need it (such as <c>UseConnectionStrings</c>) first.</returns>
    /// <remarks>
    /// <para>
    /// A context that is not pooled is created with its options and any other services its constructor needs, and
    /// has them as its application service provider, as with <c>AddDbContext</c>: the scoped
    /// <typeparamref name="TContext"/> from its scope, and one from the factory from the root provider, as EF Core's
    /// <c>AddDbContextFactory</c> does. Creating a context without a current tenant throws
    /// <see cref="TenantNotResolvedException"/>, so <c>dotnet ef</c> needs an <c>IDesignTimeDbContextFactory</c>
    /// for the context.
    /// </para>
    /// <para>
    /// The options get <c>UseTenantry()</c> before <paramref name="configure"/> runs, so interceptors added there
    /// (an audit log, say) see new entities already stamped with their tenant. For the same reason, an interceptor
    /// added there that changes what a save writes (a soft delete) runs after Tenantry's checks and is not checked.
    /// </para>
    /// <para>
    /// When the provider cannot read connection strings synchronously
    /// (<see cref="ITenantConnectionStringProvider{TKey}.CanGetSynchronously"/>), a context created synchronously, the
    /// scoped <typeparamref name="TContext"/> among them, reads its connection string when it first opens a
    /// connection, so only asynchronous EF Core calls work on it.
    /// </para>
    /// <para>
    /// A guard checks each context before it opens a connection and before every command it runs, including on a
    /// connection that is already open: the connection must have been set for the context (and, pooled, for its
    /// current lease) and for the tenant that is current now. Otherwise it throws
    /// <see cref="TenantIsolationViolationException"/> rather than use another tenant's database.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="ITenantConnectionStringProvider{TKey}"/> is registered yet, the one registered is scoped or
    /// transient, or <typeparamref name="TContext"/> is already registered this way.
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddTenantry&lt;Guid&gt;(tenant =&gt; tenant
    ///     .ResolveFromHeader("X-Tenant-Id")
    ///     .UseStore&lt;AppTenantStore&gt;()
    ///     .UseConnectionStrings(options =&gt;
    ///         options.GetConnectionString = t =&gt; $"Server=db;Database=app_{t.TenantId};Integrated Security=true")
    ///     .AddDbContextPerTenantDatabase&lt;AppDbContext&gt;((sp, options) =&gt; options.UseSqlServer(), pooled: true));
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(EfCoreRequirements.UnreferencedCode)]
    [RequiresDynamicCode(EfCoreRequirements.DynamicCode)]
    public static ITenantBuilder AddDbContextPerTenantDatabase<[DynamicallyAccessedMembers(ContextMembers)] TContext>(
        this ITenantBuilder builder,
        Action<IServiceProvider, DbContextOptionsBuilder> configure,
        bool pooled = false,
        int poolSize = 1024)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(poolSize);

        builder.Add(new TenantDatabaseRegistration<TContext>(configure, pooled, poolSize));
        return builder;
    }

    private sealed class TenantDatabaseRegistration<[DynamicallyAccessedMembers(ContextMembers)] TContext>(
        Action<IServiceProvider, DbContextOptionsBuilder> configure,
        bool pooled,
        int poolSize)
        : ITenantRegistration
        where TContext : DbContext
    {
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "AddDbContextPerTenantDatabase, which adds this registration, carries the annotation.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "AddDbContextPerTenantDatabase, which adds this registration, carries the annotation.")]
        public void Apply<TKey>(ITenantBuilder<TKey> tenant)
            where TKey : IEquatable<TKey>, IParsable<TKey>
        {
            var services = tenant.Services;

            // The registration that resolves: the last one without a key.
            var provider = services.LastOrDefault(d => d.ServiceType == typeof(ITenantConnectionStringProvider<TKey>) && !d.IsKeyedService);

            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"AddDbContextPerTenantDatabase<{typeof(TContext).Name}> connects each context to its tenant's " +
                    "database, so it needs the tenants' connection strings: call UseConnectionStrings before it, in " +
                    "the same AddTenantry.");
            }

            // The contexts' factory and the guard are singletons, which would keep one scoped or transient provider,
            // and what it depends on, for the application's lifetime.
            if (provider.Lifetime != ServiceLifetime.Singleton)
            {
                throw new InvalidOperationException(
                    $"AddDbContextPerTenantDatabase<{typeof(TContext).Name}> reads the tenants' connection strings " +
                    $"from a singleton, and ITenantConnectionStringProvider<{typeof(TKey).Name}> is registered as " +
                    $"{provider.Lifetime.ToString().ToLowerInvariant()}. Register it as a singleton, or with " +
                    "UseConnectionStrings, and have it create a scope for any scoped service it needs.");
            }

            if (services.Any(d => d.ServiceType == typeof(TenantDatabaseContexts<TContext, TKey>)))
            {
                throw new InvalidOperationException(
                    $"AddDbContextPerTenantDatabase<{typeof(TContext).Name}> was already called. Register each context type once.");
            }

            services.AddSingleton(sp => new TenantDatabaseContexts<TContext, TKey>(sp, configure, pooled, poolSize));
            services.AddSingleton<IDbContextFactory<TContext>>(sp =>
                new TenantDatabaseDbContextFactory<TContext, TKey>(sp.GetRequiredService<TenantDatabaseContexts<TContext, TKey>>(), sp));
            services.AddScoped(sp => sp.GetRequiredService<TenantDatabaseContexts<TContext, TKey>>().Create(sp));
        }
    }
}
