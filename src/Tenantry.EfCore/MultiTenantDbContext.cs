using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;

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
}
