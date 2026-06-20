using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Tests.BaseClass;

/// <summary>
/// Verifies that <see cref="Tenantry.EfCore.MultiTenantDbContext{TKey}"/> when combined with the interceptor
/// automatically stamps and validates TenantId on SaveChanges — matching the guarantee of the interceptor path.
/// </summary>
public sealed class BaseClassStampingTests
{
    [Fact]
    public async Task SaveChanges_StampsTenantIdOnAddedEntity()
    {
        // Arrange
        TestTenantContext ctx = TestTenantContext.For("acme");
        await using SqliteConnection conn = DbContextFactory.CreateSharedConnection();
        // BaseClassTestDbContext needs the interceptor for write isolation
        await using BaseClassTestDbContext db = await DbContextFactory.CreateBaseClassContextAsync(ctx, conn);

        // No TenantId set — the interceptor should stamp it automatically.
        Order order = new() { Description = "Auto-stamped" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Assert
        order.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task SaveChanges_StampedTenantId_IsVisibleViaQueryFilter()
    {
        // Arrange
        TestTenantContext ctx = TestTenantContext.For("acme");
        await using SqliteConnection conn = DbContextFactory.CreateSharedConnection();
        await using BaseClassTestDbContext db = await DbContextFactory.CreateBaseClassContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync();

        // Act — query filter should return the order for acme
        List<Order> results = await db.Orders.AsNoTracking().ToListAsync();

        // Assert
        results.Should().HaveCount(1);
        results[0].TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task SaveChanges_WhenEntityAddedWithWrongTenantId_OverridesWithCurrentTenant()
    {
        TestTenantContext ctx = TestTenantContext.For("acme");
        await using SqliteConnection conn = DbContextFactory.CreateSharedConnection();
        await using BaseClassTestDbContext db = await DbContextFactory.CreateBaseClassContextAsync(ctx, conn);

        Order order = new() { Description = "Wrong tenant", TenantId = "attacker" };
        db.Orders.Add(order);

        await db.SaveChangesAsync();

        order.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task SaveChanges_ThrowsOnCrossTenantModify()
    {
        // Arrange — seed an entity as "acme"
        TestTenantContext ctx = TestTenantContext.For("acme");
        await using SqliteConnection conn = DbContextFactory.CreateSharedConnection();
        await using BaseClassTestDbContext db = await DbContextFactory.CreateBaseClassContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync();

        // Arrange — load the entity and switch to a different tenant
        Order seededOrder = await db.Orders.IgnoreQueryFilters().FirstAsync();
        ctx.As("globex");

        // Manually set a wrong TenantId to simulate loading across tenant boundary
        db.Orders.Attach(seededOrder);
        seededOrder.Description = "Tampered";

        // Act & Assert
        await db.Invoking(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }
}
