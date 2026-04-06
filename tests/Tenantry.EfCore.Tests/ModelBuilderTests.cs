using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore.Tests.Infrastructure;

namespace Tenantry.EfCore.Tests;

// Non-tenanted entity — intentionally does NOT implement ITenantScoped<string>.
// ApplyTenantFilters must skip it; all rows remain visible regardless of tenant.
internal sealed class NonTenantedProduct
{
    public int Id { get; set; }
    
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;
}

internal sealed class MixedDbContext(
    DbContextOptions<MixedDbContext> options,
    ITenantContext<string> tenantContext)
    : DbContext(options), ITenantAwareDbContext<string>
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<NonTenantedProduct> Products => Set<NonTenantedProduct>();

    public string? CurrentTenantId => tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Only Order gets a filter; NonTenantedProduct is skipped
        modelBuilder.ApplyTenantFilters<string, MixedDbContext>(this);
    }
}

/// <summary>
/// Proves that non-tenanted entities in the same DbContext are not filtered:
/// all rows are visible regardless of the current tenant scope.
/// </summary>
public sealed class ModelBuilderTests
{
    [Fact]
    public async Task NonTenantedEntity_NoQueryFilterApplied_AllRowsVisible()
    {
        TestTenantContext ctx = new();
        await using var conn = DbContextFactory.CreateSharedConnection();

        var options =
            new DbContextOptionsBuilder<MixedDbContext>()
                .UseSqlite(conn)
                .Options;

        await using MixedDbContext db = new(options, ctx);
        await db.Database.EnsureCreatedAsync();

        // Seed products (no tenant scope needed)
        db.Products.AddRange(
            new NonTenantedProduct { Name = "Widget" },
            new NonTenantedProduct { Name = "Gadget" });
        await db.SaveChangesAsync();

        // Query from acme scope
        ctx.As("acme");
        var acmeView = await db.Products.AsNoTracking().ToListAsync();

        // Query from globex scope
        ctx.As("globex");
        var globexView = await db.Products.AsNoTracking().ToListAsync();

        // Both scopes see all products — no filter was applied
        acmeView.Should().HaveCount(2);
        globexView.Should().HaveCount(2);
    }

    [Fact]
    public async Task TenantedEntity_InSameDbContext_IsFilteredNormally()
    {
        TestTenantContext ctx = new();
        await using var conn = DbContextFactory.CreateSharedConnection();

        var options =
            new DbContextOptionsBuilder<MixedDbContext>()
                .UseSqlite(conn)
                .Options;

        await using MixedDbContext db = new(options, ctx);
        await db.Database.EnsureCreatedAsync();

        // Seed orders under acme (manually set TenantId since we're not using the interceptor)
        ctx.As("acme");
        db.Orders.Add(new Order { TenantId = "acme", Description = "Acme order" });
        await db.SaveChangesAsync();

        ctx.As("globex");
        var globexOrders = await db.Orders.AsNoTracking().ToListAsync();

        globexOrders.Should().BeEmpty(); // filter applied, globex sees nothing
    }
}
