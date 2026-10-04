using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace TenantryWorker;

// Tenant-owned: every query is filtered by TenantId, and Tenantry stamps it when the row is saved.
public sealed class Order : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;
}

// A plain DbContext: UseTenantry() in Program.cs isolates its tenant-owned entities.
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    // Every query is filtered by TenantId, so lead indexes with it.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>().HasIndex(order => new { order.TenantId, order.Id });
}

// A message carries only its tenant's id, as one from a queue does.
public sealed record OrderMessage(string TenantId, string Description);
