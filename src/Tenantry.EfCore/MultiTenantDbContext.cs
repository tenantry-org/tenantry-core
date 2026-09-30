using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore;

/// <summary>
/// Optional base <see cref="DbContext"/> that automatically applies tenant query filters
/// in <c>OnModelCreating</c>.
/// </summary>
/// <typeparam name="TKey">
/// The tenant identifier type. See <see cref="ITenantScoped{TKey}"/> for constraints.
/// </typeparam>
/// <remarks>
/// <para>
/// This class is a convenience for greenfield projects.
/// </para>
/// <para>
/// To use: derive from <see cref="MultiTenantDbContext{TKey}"/>, give your context a constructor that takes
/// only its <see cref="DbContextOptions{TContext}"/>, and call <c>base.OnModelCreating(modelBuilder)</c> at the
/// end of your override, after your own configuration. The current tenant comes from Tenantry's ambient <see cref="ITenantContext{TKey}"/>,
/// resolved from the application service provider, so the same instance serves whichever tenant is active
/// when it runs a query or saves.
/// </para>
/// <para>
/// <strong>DbContext pooling is supported.</strong> Register with <c>AddDbContextPool</c> or
/// <c>AddPooledDbContextFactory</c> and call <c>options.AddTenantInterceptors(sp)</c> in the options callback:
/// EF Core does not let <see cref="OnConfiguring"/> change the options of a pooled context, so the
/// self-wiring described below cannot run there, and a pooled context without the interceptors fails on
/// first use instead of saving without isolation.
/// </para>
/// <para>
/// <strong>Isolation is self-wiring for non-pooled contexts.</strong> When you call
/// <c>AddEfCoreIsolation()</c> and register this context through <c>AddDbContext</c> (which supplies an
/// application service provider), the tenant interceptors are attached automatically in
/// <see cref="OnConfiguring"/> — you do <em>not</em> also need <c>options.AddTenantInterceptors(sp)</c>.
/// This prevents the silent-isolation-loss failure mode of forgetting that wiring step. A
/// <strong>raw <see cref="DbContext"/></strong> that does not derive from this base class must still call
/// <c>options.AddTenantInterceptors(sp)</c> in its registration callback.
/// </para>
/// <code>
/// public class AppDbContext(DbContextOptions&lt;AppDbContext&gt; options) : MultiTenantDbContext&lt;Guid&gt;(options)
/// {
///     public DbSet&lt;Order&gt; Orders =&gt; Set&lt;Order&gt;();
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         // ... your entity configuration
///         base.OnModelCreating(modelBuilder); // last: applies tenant filters
///     }
/// }
///
/// // Either
/// builder.Services.AddDbContext&lt;AppDbContext&gt;(options =&gt; options.UseSqlServer(connectionString));
/// // or, pooled
/// builder.Services.AddDbContextPool&lt;AppDbContext&gt;((sp, options) =&gt;
///     options.UseSqlServer(connectionString).AddTenantInterceptors(sp));
/// </code>
/// </remarks>
[RequiresUnreferencedCode("EF Core is not fully compatible with trimming.")]
[RequiresDynamicCode("EF Core is not fully compatible with NativeAOT.")]
public abstract class MultiTenantDbContext<TKey> : DbContext, ITenantAwareDbContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly IServiceProvider? _applicationServiceProvider;
    private ITenantContext<TKey>? _tenantContext;

    /// <summary>
    /// Initialises a new instance that resolves the ambient <see cref="ITenantContext{TKey}"/> from the
    /// application service provider on first use. Use this constructor for pooled contexts.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    protected MultiTenantDbContext(DbContextOptions options)
        : base(options)
    {
        _applicationServiceProvider = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
    }

    /// <summary>
    /// Initialises a new instance that uses the given <see cref="ITenantContext{TKey}"/>, for contexts created
    /// outside dependency injection.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    /// <param name="tenantContext">Supplies the current tenant.</param>
    protected MultiTenantDbContext(DbContextOptions options, ITenantContext<TKey> tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the ambient tenant each time it is accessed. EF Core re-evaluates this property on every query
    /// execution because it accesses a <c>DbContext</c> property, so the filter always reflects the tenant
    /// active at that moment, including when a pooled instance is reused for another tenant.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="ITenantContext{TKey}"/> was passed to the constructor and none is registered in the
    /// application service provider.
    /// </exception>
    public TKey? CurrentTenantId => TenantContext.CurrentTenantId;

    // Tenantry registers ITenantContext<TKey> as a singleton over ambient (AsyncLocal) state, so resolving it
    // once and keeping it across pooled leases is safe.
    private ITenantContext<TKey> TenantContext =>
        _tenantContext ??= _applicationServiceProvider?.GetService<ITenantContext<TKey>>()
            ?? throw new InvalidOperationException(
                $"{GetType().Name} has no ITenantContext<{typeof(TKey).Name}>. Register Tenantry " +
                "(AddTenantry or AddTenantryCore) and create the context through AddDbContext, AddDbContextPool " +
                "or AddPooledDbContextFactory, or pass an ITenantContext to the base constructor.");

    /// <inheritdoc />
    /// <remarks>
    /// Applies tenant query filters to all <see cref="ITenantScoped{TKey}"/> types. Call
    /// <c>base.OnModelCreating(modelBuilder)</c> at the end of your override, after your own configuration:
    /// an entity type added after it gets no tenant filter, and a <c>HasQueryFilter</c> call after it can replace the
    /// tenant filter. The tenant interceptors throw on the first query or save of such a model.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyTenantFilters<TKey, MultiTenantDbContext<TKey>>(this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Self-wires the tenant <c>SaveChanges</c> interceptor so that deriving from this base class plus
    /// calling <c>AddEfCoreIsolation()</c> is sufficient — the separate
    /// <c>options.AddTenantInterceptors(sp)</c> step becomes optional. The interceptor is resolved from
    /// the application service provider this context was built with; this is a no-op when no provider is
    /// available (e.g. a hand-built <see cref="DbContextOptionsBuilder"/>) or when isolation was not
    /// registered. The wiring is idempotent, so an explicit <c>AddTenantInterceptors(sp)</c> call still
    /// works without attaching the interceptor twice.
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        var serviceProvider = optionsBuilder.Options
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

        if (serviceProvider?.GetService<ITenantInterceptorConfigurator>() is { } configurator)
            configurator.AddInterceptors(optionsBuilder, serviceProvider);
    }
}
