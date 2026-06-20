using AwesomeAssertions;

namespace Tenantry.EfCore.Tests.BaseClass;

/// <summary>
/// Verifies that the optional MultiTenantDbContext base class path provides the
/// same query filter isolation as the interceptor path.
/// </summary>
public sealed class BaseClassIsolationTests
{
    [Fact]
    public async Task Query_WhenTwoTenantsHaveData_ReturnsOnlyCurrentTenantData()
    {
        // Arrange — single mutable context, same connection
        TestTenantContext ctx = new();
        await using var conn = DbContextFactory.CreateSharedConnection();

        ctx.As("acme");
        await using var db = await DbContextFactory.CreateBaseClassContextAsync(ctx, conn);

        // Interceptor auto-stamps TenantId via SaveChanges
        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync();

        ctx.As("globex");
        db.Orders.Add(new Order { Description = "Globex order" });
        await db.SaveChangesAsync();

        // Act
        ctx.As("acme");
        var acmeOrders = await db.Orders.AsNoTracking().ToListAsync();

        ctx.As("globex");
        var globexOrders = await db.Orders.AsNoTracking().ToListAsync();

        // Assert
        acmeOrders.Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be("acme"));
        globexOrders.Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be("globex"));
    }
}
