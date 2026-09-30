using AwesomeAssertions;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Verifies that the interceptor stamps TenantId on Added entities automatically.
/// </summary>
public sealed class InterceptorStampingTests
{
    [Fact]
    public async Task SaveChanges_WhenEntityAdded_StampsTenantId()
    {
        // Arrange
        var ctx = TestTenantContext.For("acme");
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        Order order = new() { Description = "Test order" };
        db.Orders.Add(order);

        // Act
        await db.SaveChangesAsync();

        // Assert
        order.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task SaveChanges_WhenEntityAddedWithAnotherTenantsId_ThrowsAndWritesNothing()
    {
        // A caller that names another tenant on a new entity (for example from a request body) is rejected rather
        // than silently moved to the current tenant.
        var ctx = TestTenantContext.For("acme");
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "Test order", TenantId = "attacker" });

        var thrown = await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        thrown.Which.Kind.Should().Be(TenantIsolationViolationKind.EntityWrite);
        thrown.Which.OffendingTenantId.Should().Be("attacker");
        thrown.Which.ExpectedTenantId.Should().Be("acme");
        (await db.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveChanges_WhenMultipleEntitiesAdded_AllGetStamped()
    {
        // Arrange
        var ctx = TestTenantContext.For("globex");
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        Order[] orders =
        [
            new Order { Description = "First" },
            new Order { Description = "Second" },
            new Order { Description = "Third" },
        ];

        db.Orders.AddRange(orders);

        // Act
        await db.SaveChangesAsync();

        // Assert
        orders.Should().AllSatisfy(o => o.TenantId.Should().Be("globex"));
    }
}
