using Microsoft.EntityFrameworkCore;

namespace Tenantry.Samples.EfCoreConsole;

/// <summary>
/// A tenant-aware <see cref="DbContext"/>: a plain one. <c>UseTenantry()</c>, where Program.cs registers it, finds every
/// <c>ITenantEntity&lt;Guid&gt;</c> entity type and adds the per-tenant query filter after this configuration.
/// </summary>
public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>(order => order.Property(o => o.Description).HasMaxLength(200));
}
