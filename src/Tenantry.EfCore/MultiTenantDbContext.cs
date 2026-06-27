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
/// To use: derive from <see cref="MultiTenantDbContext{TKey}"/>, call <c>base.OnModelCreating(modelBuilder)</c>
/// at the start of your override, and inject <see cref="ITenantContext{TKey}"/> into the constructor.
/// </para>
/// <para>
/// <strong>Isolation is self-wiring for derived contexts.</strong> When you call
/// <c>AddEfCoreIsolation()</c> and register this context through <c>AddDbContext</c> (which supplies an
/// application service provider), the tenant <c>SaveChanges</c> interceptor is attached automatically in
/// <see cref="OnConfiguring"/> — you do <em>not</em> also need <c>options.AddTenantInterceptors(sp)</c>.
/// This prevents the silent-isolation-loss failure mode of forgetting that wiring step. A
/// <strong>raw <see cref="DbContext"/></strong> that does not derive from this base class must still call
/// <c>options.AddTenantInterceptors(sp)</c> in its <c>AddDbContext</c> callback.
/// </para>
/// <code>
/// public class AppDbContext : MultiTenantDbContext&lt;Guid&gt;
/// {
///     public DbSet&lt;Order&gt; Orders =&gt; Set&lt;Order&gt;();
///
///     public AppDbContext(DbContextOptions&lt;AppDbContext&gt; options, ITenantContext&lt;Guid&gt; tenantContext)
///         : base(options, tenantContext) { }
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder); // applies tenant filters
///         // ... your entity configuration
///     }
/// }
/// </code>
/// </remarks>
[RequiresUnreferencedCode("EF Core is not fully compatible with trimming.")]
[RequiresDynamicCode("EF Core is not fully compatible with NativeAOT.")]
public abstract class MultiTenantDbContext<TKey> : DbContext, ITenantAwareDbContext<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    private readonly ITenantContext<TKey> _tenantContext;

    /// <summary>
    /// Initialises a new instance of <see cref="MultiTenantDbContext{TKey}"/>.
    /// </summary>
    protected MultiTenantDbContext(DbContextOptions options, ITenantContext<TKey> tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the injected <see cref="ITenantContext{TKey}"/>. EF Core re-evaluates
    /// this property on every query execution because it accesses a <c>DbContext</c>
    /// property, ensuring the filter always reflects the current tenant.
    /// </remarks>
    public TKey? CurrentTenantId => _tenantContext.CurrentTenantId;

    /// <inheritdoc />
    /// <remarks>
    /// Applies tenant query filters to all <see cref="ITenantScoped{TKey}"/> types.
    /// Always call <c>base.OnModelCreating(modelBuilder)</c> first in derived classes.
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
