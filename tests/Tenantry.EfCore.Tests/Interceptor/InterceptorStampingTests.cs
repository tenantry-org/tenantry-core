using AwesomeAssertions;
using Tenantry.EfCore.Tests.Infrastructure;

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
    public async Task SaveChanges_WhenEntityAddedWithWrongTenantId_OverridesWithCurrentTenant()
    {
        // Arrange — consumer set a "wrong" TenantId; the interceptor must override it.
        var ctx = TestTenantContext.For("acme");
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        Order order = new() { Description = "Test order", TenantId = "attacker" };
        db.Orders.Add(order);

        // Act
        await db.SaveChangesAsync();

        // Assert — interceptor always overrides TenantId on Added entities
        order.TenantId.Should().Be("acme");
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
