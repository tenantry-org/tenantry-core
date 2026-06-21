using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.EfCore;

namespace Tenantry.Samples.EfCoreConsole;

/// <summary>
/// A tenant-aware <see cref="DbContext"/>.
/// <para>
/// This sample uses the optional <see cref="MultiTenantDbContext{TKey}"/> base class, which
/// implements <see cref="ITenantAwareDbContext{TKey}"/> and applies the tenant query filters
/// for you in <c>OnModelCreating</c>. If you already have a <see cref="DbContext"/> you cannot
/// change the base of, implement <see cref="ITenantAwareDbContext{TKey}"/> directly and call
/// <c>modelBuilder.ApplyTenantFilters&lt;TKey, TContext&gt;(this)</c> yourself — see the
/// EfCoreWeb sample for that approach.
/// </para>
/// </summary>
public sealed class SampleDbContext(
    DbContextOptions<SampleDbContext> options,
    ITenantContext<Guid> tenantContext)
    : MultiTenantDbContext<Guid>(options, tenantContext)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Always call the base first — it discovers every ITenantScoped<Guid> entity and
        // applies the per-tenant global query filter.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(order =>
        {
            order.Property(o => o.Description).HasMaxLength(200);
        });
    }
}
