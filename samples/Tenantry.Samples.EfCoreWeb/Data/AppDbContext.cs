using Microsoft.EntityFrameworkCore;
using Tenantry.Samples.EfCoreWeb.Entities;

namespace Tenantry.Samples.EfCoreWeb.Data;

// A plain DbContext: UseTenantry() in Program.cs isolates the tenant-owned entities (ITenantEntity<string>).
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Tenant-owned entities, isolated per tenant
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // Entities every tenant shares, marked [SharedAcrossTenants]
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    // The tenant registry, shared too
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Tenant ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasKey(t => t.TenantId);
            b.Property(t => t.TenantId).HasMaxLength(64);
            b.Property(t => t.Name).HasMaxLength(200);
            b.Property(t => t.Description).HasMaxLength(500);
            b.Property(t => t.SubscriptionTier).HasMaxLength(50).HasDefaultValue("Free");
        });

        // ── Order ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Order>(b =>
        {
            b.Property(o => o.OrderNumber).HasMaxLength(100);
            b.Property(o => o.Status).HasMaxLength(50);
            b.HasIndex(o => o.TenantId);   // Tenantry adds no index; every filtered query compares TenantId
            b.HasIndex(o => o.OrderNumber);
            b.HasIndex(o => o.CreatedAt);
            b.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── OrderItem ─────────────────────────────────────────────────────────
        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasIndex(i => i.TenantId);
            b.HasOne(i => i.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Product ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Product>(b =>
        {
            b.Property(p => p.Name).HasMaxLength(200);
            b.Property(p => p.Description).HasMaxLength(1000);
            b.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Category ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Category>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(100);
            b.Property(c => c.Description).HasMaxLength(500);
        });
    }
}
