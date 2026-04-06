using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Tenantry.EfCore.Tests.Infrastructure;

namespace Tenantry.EfCore.Tests;

/// <summary>
/// Proves that query filters survive aggregates, composed Where, GroupBy, and Select.
/// </summary>
public sealed class QueryFilterBypassTests
{
    private static async Task<(TestDbContext db, TestTenantContext ctx, SqliteConnection conn)> SeedTwoTenantsAsync()
    {
        TestTenantContext ctx = new();
        var conn = DbContextFactory.CreateSharedConnection();

        ctx.As("acme");
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.AddRange(
            new Order { Description = "Acme A" },
            new Order { Description = "Acme B" });
        await db.SaveChangesAsync();

        ctx.As("globex");
        db.Orders.Add(new Order { Description = "Globex X" });
        await db.SaveChangesAsync();

        return (db, ctx, conn);
    }

    [Fact]
    public async Task Query_CountByTenant_ReturnsCorrectCount()
    {
        (var db, var ctx, var conn) = await SeedTwoTenantsAsync();
        await using (db)
        await using (conn)
        {
            ctx.As("acme");
            var acmeCount = await db.Orders.CountAsync();

            ctx.As("globex");
            var globexCount = await db.Orders.CountAsync();

            acmeCount.Should().Be(2);
            globexCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Query_WhereOnTop_DoesNotLeakCrossTenantRows()
    {
        (var db, var ctx, var conn) = await SeedTwoTenantsAsync();
        await using (db)
        await using (conn)
        {
            ctx.As("acme");
            var orders = await db.Orders
                .Where(o => o.Description != string.Empty)
                .AsNoTracking()
                .ToListAsync();

            orders.Should().HaveCount(2).And.AllSatisfy(o => o.TenantId.Should().Be("acme"));
        }
    }

    [Fact]
    public async Task Query_GroupBy_DoesNotLeakCrossTenantRows()
    {
        (var db, var ctx, var conn) = await SeedTwoTenantsAsync();
        await using (db)
        await using (conn)
        {
            ctx.As("acme");
            var groupCount = await db.Orders
                .GroupBy(o => o.TenantId)
                .CountAsync();

            // Only one tenant group visible
            groupCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Query_Select_DoesNotLeakCrossTenantRows()
    {
        (var db, var ctx, var conn) = await SeedTwoTenantsAsync();
        await using (db)
        await using (conn)
        {
            ctx.As("acme");
            var descriptions = await db.Orders
                .Select(o => o.Description)
                .ToListAsync();

            descriptions.Should().HaveCount(2)
                .And.NotContain("Globex X");
        }
    }
}
