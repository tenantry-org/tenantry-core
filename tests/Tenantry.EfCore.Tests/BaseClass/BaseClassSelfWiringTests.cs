using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;

namespace Tenantry.EfCore.Tests.BaseClass;

/// <summary>
/// Fail-closed guard for the silent-isolation-loss failure mode. A
/// <see cref="Tenantry.EfCore.MultiTenantDbContext{TKey}"/>-derived context configured with ONLY
/// <c>AddEfCoreIsolation()</c> — and crucially <em>without</em> the explicit
/// <c>options.AddTenantInterceptors(sp)</c> step — must still stamp TenantId and reject cross-tenant
/// writes, because <c>OnConfiguring</c> self-wires the interceptor from the application service provider.
/// </summary>
public sealed class BaseClassSelfWiringTests
{
    [Fact]
    public async Task SaveChanges_WithoutExplicitInterceptorWiring_StampsTenantId()
    {
        var tenant = TestTenantContext.For("acme");
        await using var sp = BuildProvider(tenant);
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await CreateSelfWiringContextAsync(sp, tenant, conn);

        var order = new Order { Description = "Auto-stamped" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        order.TenantId.Should().Be("acme",
            "OnConfiguring self-wires the interceptor when only AddEfCoreIsolation() was called");
    }

    [Fact]
    public async Task SaveChanges_WithoutExplicitInterceptorWiring_RejectsCrossTenantModify()
    {
        var tenant = TestTenantContext.For("acme");
        await using var sp = BuildProvider(tenant);
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await CreateSelfWiringContextAsync(sp, tenant, conn);

        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync();

        var seeded = await db.Orders.IgnoreQueryFilters().FirstAsync();
        tenant.As("globex");
        db.Orders.Attach(seeded);
        seeded.Description = "Tampered";

        await db.Invoking(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }

    private static ServiceProvider BuildProvider(TestTenantContext tenant)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenantryCore<string>(b => b.AddEfCoreIsolation());
        // Drive the interceptor's tenant from the mutable test context (registered last, so it wins).
        services.AddSingleton<ITenantContext<string>>(tenant);

        return services.BuildServiceProvider();
    }

    private static async Task<BaseClassTestDbContext> CreateSelfWiringContextAsync(
        IServiceProvider sp, ITenantContext<string> tenant, SqliteConnection connection)
    {
        // Options carry the application service provider (exactly as AddDbContext does) but deliberately
        // OMIT AddTenantInterceptors(sp); OnConfiguring must attach the interceptor itself.
        var options = new DbContextOptionsBuilder<BaseClassTestDbContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(sp)
            .Options;

        var ctx = new BaseClassTestDbContext(options, tenant);
        await ctx.Database.EnsureCreatedAsync();

        return ctx;
    }
}
