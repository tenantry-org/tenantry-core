using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Tests.GuidKey;

/// <summary>
/// Verifies that the generic <c>TKey</c> constraint works correctly with <see cref="Guid"/> keys —
/// covering query filtering, interceptor stamping, cross-tenant write prevention, and
/// <see cref="System.Threading.AsyncLocal{T}"/> isolation under concurrent load.
/// </summary>
public sealed class GuidKeyEfCoreTests
{
    [Fact]
    public async Task QueryFilter_ReturnsOnlyCurrentTenantOrders()
    {
        // Arrange
        GuidTestTenantContext ctx = new();
        Guid acme = Guid.NewGuid();
        Guid globex = Guid.NewGuid();

        await using SqliteConnection conn = DbContextFactory.CreateSharedConnection();

        ctx.As(acme);
        await using GuidTestDbContext db = await DbContextFactory.CreateGuidInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new GuidOrder { Description = "Acme order 1" });
        db.Orders.Add(new GuidOrder { Description = "Acme order 2" });
        await db.SaveChangesAsync();

        ctx.As(globex);
        db.Orders.Add(new GuidOrder { Description = "Globex order" });
        await db.SaveChangesAsync();

        // Act
        ctx.As(acme);
        List<GuidOrder> acmeOrders = await db.Orders.AsNoTracking().ToListAsync();

        ctx.As(globex);
        List<GuidOrder> globexOrders = await db.Orders.AsNoTracking().ToListAsync();

        // Assert
        acmeOrders.Should().HaveCount(2).And.AllSatisfy(o => o.TenantId.Should().Be(acme));
        globexOrders.Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be(globex));
    }

    [Fact]
    public async Task Interceptor_StampsTenantIdOnAddedOrder()
    {
        // Arrange
        GuidTestTenantContext ctx = new();
        Guid tenantId = Guid.NewGuid();
        ctx.As(tenantId);

        (GuidTestDbContext db, SqliteConnection conn) = await DbContextFactory.CreateIsolatedGuidInterceptorContextAsync(ctx);
        await using (conn)
        await using (db)
        {
            GuidOrder order = new() { Description = "To be stamped" };
            db.Orders.Add(order);

            // Act
            await db.SaveChangesAsync();

            // Assert — interceptor must have stamped the Guid TenantId
            order.TenantId.Should().Be(tenantId);
        }
    }

    [Fact]
    public async Task Interceptor_ThrowsOnCrossTenantModify()
    {
        // Arrange — seed as acme
        GuidTestTenantContext ctx = new();
        Guid acme = Guid.NewGuid();
        Guid globex = Guid.NewGuid();

        await using SqliteConnection conn = DbContextFactory.CreateSharedConnection();

        ctx.As(acme);
        await using GuidTestDbContext db = await DbContextFactory.CreateGuidInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new GuidOrder { Description = "Acme order" });
        await db.SaveChangesAsync();

        // Load the entity, then switch tenant
        GuidOrder acmeOrder = await db.Orders.IgnoreQueryFilters().FirstAsync();
        ctx.As(globex);
        acmeOrder.Description = "Tampered by globex";

        // Act & Assert — interceptor must block the write
        await db.Invoking(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }

    [Fact]
    public async Task AsyncLocalIsolation_ConcurrentRequests_EachSeeOnlyTheirData()
    {
        // Arrange — two separate databases, each pre-seeded for one tenant
        Guid acme = Guid.NewGuid();
        Guid globex = Guid.NewGuid();

        // Seed acme
        GuidTestTenantContext acmeCtx = new();
        acmeCtx.As(acme);
        (GuidTestDbContext acmeDb, SqliteConnection acmeConn) =
            await DbContextFactory.CreateIsolatedGuidInterceptorContextAsync(acmeCtx);

        acmeDb.Orders.Add(new GuidOrder { Description = "Acme order" });
        await acmeDb.SaveChangesAsync();

        // Seed globex
        GuidTestTenantContext globexCtx = new();
        globexCtx.As(globex);
        (GuidTestDbContext globexDb, SqliteConnection globexConn) =
            await DbContextFactory.CreateIsolatedGuidInterceptorContextAsync(globexCtx);

        globexDb.Orders.Add(new GuidOrder { Description = "Globex order" });
        await globexDb.SaveChangesAsync();

        // Act — query both tenants concurrently
        Task<List<GuidOrder>> acmeTask = Task.Run(async () =>
        {
            acmeCtx.As(acme);
            return await acmeDb.Orders.AsNoTracking().ToListAsync();
        });

        Task<List<GuidOrder>> globexTask = Task.Run(async () =>
        {
            globexCtx.As(globex);
            return await globexDb.Orders.AsNoTracking().ToListAsync();
        });

        List<GuidOrder>[] results = await Task.WhenAll(acmeTask, globexTask);

        // Assert — AsyncLocal must not have leaked between the concurrent tasks
        results[0].Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be(acme));
        results[1].Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be(globex));

        await acmeDb.DisposeAsync();
        await acmeConn.DisposeAsync();
        await globexDb.DisposeAsync();
        await globexConn.DisposeAsync();
    }
}
