using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore;
using Tenantry.Samples.EfCoreWeb.Entities;

namespace Tenantry.Samples.EfCoreWeb.Data;

public class AppDbContext : DbContext, ITenantAwareDbContext<string>
{
    private readonly ITenantContext<string> _tenantContext;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext<string> tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }
    
    // Required by ITenantAwareDbContext — EF Core re-evaluates this per query
    public string? CurrentTenantId => _tenantContext.CurrentTenantId;

    // Tenanted entities — isolated per tenant
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // Non-tenanted entities — shared across all tenants
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    // Tenant registry — global, not tenanted
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply global query filters to all ITenantScoped types.
        modelBuilder.ApplyTenantFilters<string, AppDbContext>(this);

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
