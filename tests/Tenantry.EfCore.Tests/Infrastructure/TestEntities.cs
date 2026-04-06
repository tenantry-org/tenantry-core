using System.ComponentModel.DataAnnotations;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;

namespace Tenantry.EfCore.Tests.Infrastructure;

/// <summary>A simple order entity for testing.</summary>
public class Order : ITenantScoped<string>
{
    public int Id { get; set; }
    
    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;
    
    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A plain DbContext (no base class) for testing the interceptor-first path.
/// Implements <see cref="ITenantAwareDbContext{TKey}"/> so that <see cref="TenantModelBuilderExtensions.ApplyTenantFilters{TKey,TContext}"/>
/// can close the query filter over <c>this</c> rather than an external service capture.
/// EF Core re-evaluates DbContext property accesses per query, which makes the filter
/// always reflect the current tenant even though the compiled plan is cached.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext<string> tenantContext)
    : DbContext(options), ITenantAwareDbContext<string>
{
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc />
    public string? CurrentTenantId => tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyTenantFilters<string, TestDbContext>(this);
    }
}

/// <summary>
/// A DbContext that inherits from MultiTenantDbContext for testing the optional base class path.
/// </summary>
public class BaseClassTestDbContext(
    DbContextOptions<BaseClassTestDbContext> options,
    ITenantContext<string> tenantContext)
    : MultiTenantDbContext<string>(options, tenantContext)
{
    public DbSet<Order> Orders => Set<Order>();
}

// ── Guid-keyed entities and DbContext ────────────────────────────────────────

/// <summary>A simple order entity for Guid-keyed tenant tests.</summary>
public class GuidOrder : ITenantScoped<Guid>
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    
    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A plain DbContext (no base class) for testing the interceptor-first path with <see cref="Guid"/> keys.
/// </summary>
public class GuidTestDbContext(DbContextOptions<GuidTestDbContext> options, ITenantContext<Guid> tenantContext)
    : DbContext(options), ITenantAwareDbContext<Guid>
{
    public DbSet<GuidOrder> Orders => Set<GuidOrder>();

    /// <inheritdoc />
#pragma warning disable CS8766 // Nullability of return type doesn't match implicitly implemented member
    public Guid CurrentTenantId => tenantContext.CurrentTenantId;
#pragma warning restore CS8766

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyTenantFilters<Guid, GuidTestDbContext>(this);
    }
}
