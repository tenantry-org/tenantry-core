using AwesomeAssertions;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Verifies that query filters applied via the interceptor path restrict results
/// to the current tenant across all common query patterns.
/// </summary>
public sealed class InterceptorIsolationTests
{
    [Fact]
    public async Task Query_WhenTwoTenantsHaveData_ReturnsOnlyCurrentTenantData()
    {
        // Arrange — one mutable context, one connection, one database
        TestTenantContext ctx = new();
        await using var conn = DbContextFactory.CreateSharedConnection();

        ctx.As("acme");
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "Acme order 1" });
        db.Orders.Add(new Order { Description = "Acme order 2" });
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
        acmeOrders.Should().HaveCount(2).And.AllSatisfy(o => o.TenantId.Should().Be("acme"));
        globexOrders.Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be("globex"));
    }

    [Fact]
    public async Task Query_IgnoreQueryFilters_ReturnsAllTenantData()
    {
        // Arrange
        TestTenantContext ctx = new();
        await using var conn = DbContextFactory.CreateSharedConnection();

        ctx.As("acme");
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync();

        ctx.As("globex");
        db.Orders.Add(new Order { Description = "Globex order" });
        await db.SaveChangesAsync();

        // Act — explicit opt-in to bypass filters
        var allOrders = await db.Orders.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        // Assert
        allOrders.Should().HaveCount(2);
    }
}
