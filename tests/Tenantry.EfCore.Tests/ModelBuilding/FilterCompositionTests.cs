using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.ModelBuilding;

public class FilterCompositionTests
{
    [Fact]
    public async Task ApplyTenantFilters_ShouldCombineExistingFilters()
    {
        // Arrange
        var tenantContext = TestTenantContext.For("acme");
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FilterCompositionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new FilterCompositionDbContext(options, tenantContext))
        {
            await db.Database.EnsureCreatedAsync();

            // Act & Assert
            // 1. Create data for acme (not deleted)
            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Acme Active", TenantId = "acme", IsDeleted = false });
            // 2. Create data for acme (deleted)
            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Acme Deleted", TenantId = "acme", IsDeleted = true });
            // 3. Create data for other (not deleted)
            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Other Active", TenantId = "other", IsDeleted = false });

            await db.SaveChangesAsync();
        }

        await using (var db = new FilterCompositionDbContext(options, tenantContext))
        {
            // Execute query
            var results = await db.SoftDeleteOrders.ToListAsync();

            // Should only see the active order for the current tenant
            // If the filter was NOT combined, we might see "Other Active" as well (if tenant filter was skipped)
            // Or we might see "Acme Deleted" if tenant filter overwrote the soft-delete filter.
            Assert.Single(results);
            Assert.Equal("Acme Active", results[0].Description);
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_ShouldBypassBothComposedFilters()
    {
        // Arrange
        var tenantContext = TestTenantContext.For("acme");
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FilterCompositionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new FilterCompositionDbContext(options, tenantContext))
        {
            await db.Database.EnsureCreatedAsync();

            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Acme Active", TenantId = "acme", IsDeleted = false });
            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Acme Deleted", TenantId = "acme", IsDeleted = true });
            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Other Active", TenantId = "other", IsDeleted = false });

            await db.SaveChangesAsync();
        }

        await using (var db = new FilterCompositionDbContext(options, tenantContext))
        {
            // IgnoreQueryFilters should bypass both the tenant filter and the soft-delete filter
            var results = await db.SoftDeleteOrders.IgnoreQueryFilters().ToListAsync();

            Assert.Equal(3, results.Count);
        }
    }

    [Fact]
    public async Task ApplyTenantFilters_CalledTwice_ShouldBeIdempotent()
    {
        // Arrange – use a context that calls ApplyTenantFilters twice
        var tenantContext = TestTenantContext.For("acme");
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<DoubleApplyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new DoubleApplyDbContext(options, tenantContext))
        {
            await db.Database.EnsureCreatedAsync();

            db.Orders.Add(new Order { Description = "Acme Order", TenantId = "acme" });
            db.Orders.Add(new Order { Description = "Other Order", TenantId = "other" });

            await db.SaveChangesAsync();
        }

        await using (var db = new DoubleApplyDbContext(options, tenantContext))
        {
            // If the filter was applied twice, the expression tree would still evaluate
            // correctly (AND is idempotent for the same predicate), but the key check is
            // that we still get exactly the right rows — not zero (broken expression) or
            // more than expected (filter dropped).
            var results = await db.Orders.ToListAsync();

            Assert.Single(results);
            Assert.Equal("Acme Order", results[0].Description);
        }
    }

    [Fact]
    public async Task Interceptor_StampedEntity_ShouldBeVisibleThroughComposedFilter()
    {
        // Arrange — interceptor stamps TenantId, composed filter should return the stamped entity
        var tenantContext = TestTenantContext.For("acme");
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        TenantSaveChangesInterceptor<string> interceptor = new(
            tenantContext,
            new EfCoreIsolationOptions(),
            new StrictIsolationValidator<string>(NullLogger<StrictIsolationValidator<string>>.Instance),
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance);

        var options = new DbContextOptionsBuilder<FilterCompositionDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        await using (var db = new FilterCompositionDbContext(options, tenantContext))
        {
            await db.Database.EnsureCreatedAsync();

            // Add without explicitly setting TenantId — interceptor should stamp it
            db.SoftDeleteOrders.Add(new SoftDeleteOrder { Description = "Stamped Order", IsDeleted = false });
            await db.SaveChangesAsync();
        }

        await using (var db = new FilterCompositionDbContext(options, tenantContext))
        {
            var results = await db.SoftDeleteOrders.ToListAsync();

            Assert.Single(results);
            Assert.Equal("Stamped Order", results[0].Description);
            Assert.Equal("acme", results[0].TenantId);
        }
    }

    [Fact]
    public async Task ApplyTenantFilters_ShouldCombineWithComplexExistingFilter()
    {
        // Arrange — entity has a single pre-existing filter with multiple conditions
        var tenantContext = TestTenantContext.For("acme");
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MultiFilterDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new MultiFilterDbContext(options, tenantContext))
        {
            await db.Database.EnsureCreatedAsync();

            db.Items.Add(new FilterableItem { Name = "Active Published Acme", TenantId = "acme", IsDeleted = false, IsPublished = true });
            db.Items.Add(new FilterableItem { Name = "Active Draft Acme", TenantId = "acme", IsDeleted = false, IsPublished = false });
            db.Items.Add(new FilterableItem { Name = "Deleted Published Acme", TenantId = "acme", IsDeleted = true, IsPublished = true });
            db.Items.Add(new FilterableItem { Name = "Active Published Other", TenantId = "other", IsDeleted = false, IsPublished = true });

            await db.SaveChangesAsync();
        }

        await using (var db = new MultiFilterDbContext(options, tenantContext))
        {
            // Only the active, published, acme-tenant item should be returned
            var results = await db.Items.ToListAsync();

            Assert.Single(results);
            Assert.Equal("Active Published Acme", results[0].Name);
        }
    }

    [Fact]
    public async Task ApplyTenantFilters_WithUnkeyedPreExistingFilter_CombinesFilters()
    {
        // Uses an always-unkeyed HasQueryFilter (no key string argument).
        // In net10 this hits the CombineFilters path at L90-98 of TenantModelBuilderExtensions
        // (existingFilters has a null-key entry). In net8/net9 it exercises the #else merge path.
        var tenantContext = TestTenantContext.For("acme");
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<UnkeyedSoftDeleteDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new UnkeyedSoftDeleteDbContext(options, tenantContext))
        {
            await db.Database.EnsureCreatedAsync();

            db.Items.Add(new UnkeyedItem { Description = "Acme Active", TenantId = "acme", IsDeleted = false });
            db.Items.Add(new UnkeyedItem { Description = "Acme Deleted", TenantId = "acme", IsDeleted = true });
            db.Items.Add(new UnkeyedItem { Description = "Other Active", TenantId = "other", IsDeleted = false });

            await db.SaveChangesAsync();
        }

        await using (var db = new UnkeyedSoftDeleteDbContext(options, tenantContext))
        {
            var results = await db.Items.ToListAsync();

            Assert.Single(results);
            Assert.Equal("Acme Active", results[0].Description);
        }
    }

    // ── Test entities and contexts ───────────────────────────────────────────

    private class SoftDeleteOrder : Order
    {
        public bool IsDeleted { get; set; }
    }

    private class FilterableItem : ITenantScoped<string>
    {
        [Key]
        public int Id { get; set; }
        
        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
        
        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
        
        public bool IsDeleted { get; set; }
        
        public bool IsPublished { get; set; }
    }

    private class FilterCompositionDbContext(
        DbContextOptions<FilterCompositionDbContext> options,
        ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<SoftDeleteOrder> SoftDeleteOrders => Set<SoftDeleteOrder>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
#if NET10_0_OR_GREATER
            modelBuilder.Entity<SoftDeleteOrder>().HasQueryFilter("IsDeleted", e => !e.IsDeleted);
#else
            modelBuilder.Entity<SoftDeleteOrder>().HasQueryFilter(e => !e.IsDeleted);
#endif
            modelBuilder.ApplyTenantFilters<string, FilterCompositionDbContext>(this);
        }
    }

    /// <summary>
    /// A DbContext that deliberately calls ApplyTenantFilters twice to verify idempotency.
    /// </summary>
    private class DoubleApplyDbContext(
        DbContextOptions<DoubleApplyDbContext> options,
        ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<Order> Orders => Set<Order>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyTenantFilters<string, DoubleApplyDbContext>(this);
            modelBuilder.ApplyTenantFilters<string, DoubleApplyDbContext>(this);
        }
    }

    /// <summary>
    /// A DbContext with a complex pre-existing query filter (multiple conditions in one expression),
    /// verifying that ApplyTenantFilters combines correctly with compound filters.
    /// </summary>
    private class MultiFilterDbContext(
        DbContextOptions<MultiFilterDbContext> options,
        ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<FilterableItem> Items => Set<FilterableItem>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
#if NET10_0_OR_GREATER
            modelBuilder.Entity<FilterableItem>().HasQueryFilter("IsPublishedAndActive", e => !e.IsDeleted && e.IsPublished);
#else           
            modelBuilder.Entity<FilterableItem>().HasQueryFilter(e => !e.IsDeleted && e.IsPublished);
#endif
            modelBuilder.ApplyTenantFilters<string, MultiFilterDbContext>(this);
        }
    }

    private class UnkeyedItem : ITenantScoped<string>
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Description { get; set; } = string.Empty;

        public bool IsDeleted { get; set; }
    }

    /// <summary>
    /// A DbContext with an always-unkeyed pre-existing filter.
    /// In net10, this triggers the null-key merge branch (L90-98) in
    /// <c>ApplyTenantFilters</c> and exercises <c>CombineFilters</c>.
    /// </summary>
    private class UnkeyedSoftDeleteDbContext(
        DbContextOptions<UnkeyedSoftDeleteDbContext> options,
        ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<UnkeyedItem> Items => Set<UnkeyedItem>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Intentionally unkeyed on all frameworks to exercise the CombineFilters path.
            modelBuilder.Entity<UnkeyedItem>().HasQueryFilter(e => !e.IsDeleted);
            modelBuilder.ApplyTenantFilters<string, UnkeyedSoftDeleteDbContext>(this);
        }
    }
}
