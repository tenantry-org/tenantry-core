using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Creates the contexts <c>AddDbContextPerTenantDatabase</c> registers, pooled or not, and connects each one to the
/// current tenant's database.
/// </summary>
internal sealed class TenantDatabaseContexts<
    [DynamicallyAccessedMembers(TenantryEfCoreTenantBuilderExtensions.ContextMembers)] TContext, TKey>
    where TContext : DbContext
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly ITenantConnectionStringProvider<TKey> _connectionStrings;
    private readonly ITenantContext<TKey> _tenantContext;
    private readonly DbContextOptions<TContext> _options;
    private readonly PooledDbContextFactory<TContext>? _pool;
    private readonly ObjectFactory<TContext>? _activator;

    [RequiresUnreferencedCode(EfCoreRequirements.UnreferencedCode)]
    [RequiresDynamicCode(EfCoreRequirements.DynamicCode)]
    public TenantDatabaseContexts(
        IServiceProvider services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure,
        bool pooled,
        int poolSize)
    {
        _connectionStrings = services.GetRequiredService<ITenantConnectionStringProvider<TKey>>();
        _tenantContext = services.GetRequiredService<ITenantContext<TKey>>();

        DbContextOptionsBuilder<TContext> builder = new();
        builder.UseApplicationServiceProvider(services);

        // The guard goes first, so a context used under the wrong tenant is rejected before any other interceptor
        // acts on it (for example, stamping pending inserts with the current tenant). Tenantry's own interceptors come
        // next, so the application's (an audit log, say) see new entities already stamped.
        builder.AddInterceptors(new TenantDatabaseGuard<TKey>(_tenantContext, _connectionStrings));
        builder.UseTenantry();
        configure(services, builder);
        _options = builder.Options;

        if (pooled)
        {
            _pool = new PooledDbContextFactory<TContext>(_options, poolSize);
        }
        else
        {
            _activator = ActivatorUtilities.CreateFactory<TContext>([typeof(DbContextOptions<TContext>)]);
        }
    }

    /// <summary>A context connected to the current tenant's database.</summary>
    /// <param name="services">
    /// For a context that is not pooled, the services it takes in its constructor and its application service
    /// provider, as with <c>AddDbContext</c>: the scope it is created in, or the root for the factory.
    /// </param>
    public TContext Create(IServiceProvider services)
    {
        // The tenant and connection string first: without either this throws before a context is created.
        var tenant = CurrentTenant();

        // A provider that reads connection strings only asynchronously is asked when the context first opens a
        // connection (TenantDatabaseGuard), so a scoped context can be injected; only asynchronous calls work on it.
        var connectionString = _connectionStrings.CanGetSynchronously ? _connectionStrings.Get(tenant) : null;
        return Connect(_pool?.CreateDbContext() ?? New(services), tenant, connectionString);
    }

    /// <inheritdoc cref="Create"/>
    public async Task<TContext> CreateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenant = CurrentTenant();
        var connectionString = await _connectionStrings.GetAsync(tenant, cancellationToken).ConfigureAwait(false);
        var context = _pool is null
            ? New(services)
            : await _pool.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return Connect(context, tenant, connectionString);
    }

    // The application service provider is not part of EF Core's internal service provider key, so this reuses the
    // internal services of the options built above.
    private TContext New(IServiceProvider services) =>
        _activator!(services, [new DbContextOptionsBuilder<TContext>(_options).UseApplicationServiceProvider(services).Options]);

    private ITenantDescriptor<TKey> CurrentTenant() =>
        _tenantContext.CurrentTenant
        ?? throw new TenantNotResolvedException(
            $"No tenant is current, so there is no tenant database to connect this '{typeof(TContext).Name}' to. " +
            "Create it during a request (after app.UseTenantry()) or inside a scope from ITenantScopeFactory.");

    // A null connection string is read later, when the context opens a connection.
    private static TContext Connect(TContext context, ITenantDescriptor<TKey> tenant, string? connectionString)
    {
        try
        {
            // Set even when null, so a pooled context never keeps the previous lease's.
            context.Database.SetConnectionString(connectionString);
            TenantDatabaseLeases.Record(context, context.ContextId.Lease, tenant.TenantId, connectionString is null ? tenant : null);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The <see cref="IDbContextFactory{TContext}"/> <c>AddDbContextPerTenantDatabase</c> registers: it creates contexts
/// connected to the current tenant's database.
/// </summary>
internal sealed class TenantDatabaseDbContextFactory<
    [DynamicallyAccessedMembers(TenantryEfCoreTenantBuilderExtensions.ContextMembers)] TContext, TKey>(
    TenantDatabaseContexts<TContext, TKey> contexts,
    IServiceProvider services)
    : IDbContextFactory<TContext>
    where TContext : DbContext
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public TContext CreateDbContext() => contexts.Create(services);

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        contexts.CreateAsync(services, cancellationToken);
}
