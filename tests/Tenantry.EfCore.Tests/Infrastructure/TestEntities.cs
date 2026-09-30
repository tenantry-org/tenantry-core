using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace Tenantry.EfCore.Tests.Infrastructure;

/// <summary>A simple order entity for testing.</summary>
public class Order : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A plain DbContext (no base class or interface) that uses <c>UseTenantry()</c> through its options.
/// </summary>
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<NonTenant> NonTenants => Set<NonTenant>();
}

// ── Guid-keyed entities and DbContext ────────────────────────────────────────

/// <summary>A simple order entity for Guid-keyed tenant tests.</summary>
public class GuidOrder : ITenantEntity<Guid>
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }

    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A plain DbContext that uses <c>UseTenantry()</c> through its options, with <see cref="Guid"/> keys.
/// </summary>
public class GuidTestDbContext(DbContextOptions<GuidTestDbContext> options) : DbContext(options)
{
    public DbSet<GuidOrder> Orders => Set<GuidOrder>();
}

/// <summary>
/// A mapped entity that does not implement ITenantEntity — used to ensure the isolation applier
/// and validator skip non-tenant-scoped types.
/// </summary>
public class NonTenant
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;
}

