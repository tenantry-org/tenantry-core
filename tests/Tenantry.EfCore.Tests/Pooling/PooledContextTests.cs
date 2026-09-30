using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;

namespace Tenantry.EfCore.Tests.Pooling;

/// <summary>A pool-compatible context: its only constructor takes the options.</summary>
public sealed class PooledOrdersContext(DbContextOptions<PooledOrdersContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>
/// Verifies that pooled contexts using <c>UseTenantry()</c> isolate whichever tenant is active each time they are
/// used, through <c>AddDbContextPool</c> and <c>AddPooledDbContextFactory</c>.
/// </summary>
public sealed class PooledContextTests : IDisposable
{
    private readonly string _connectionString = $"DataSource=pooled-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public PooledContextTests()
    {
        // A named shared-cache in-memory database lives while one connection is open, and lets each context
        // (including concurrent ones) open its own connection.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task PooledInstance_IsolatesEachTenantItServes()
    {
        await using var services = BuildServices(pooledFactory: false);
        var tenants = services.GetRequiredService<ITenantContextSetter<string>>();
        Guid instanceId;

        using (tenants.Use(Tenant("acme")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();
            await db.Database.EnsureCreatedAsync();
            db.Orders.Add(new Order { Description = "acme order" });
            await db.SaveChangesAsync();
            instanceId = db.ContextId.InstanceId;
        }

        using (tenants.Use(Tenant("globex")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();

            db.ContextId.InstanceId.Should().Be(instanceId, "the pooled instance is reused");
            (await db.Orders.CountAsync()).Should().Be(0);

            db.Orders.Add(new Order { Description = "globex order" });
            await db.SaveChangesAsync();
        }

        using (tenants.Use(Tenant("acme")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();

            (await db.Orders.Select(o => o.Description).ToListAsync()).Should().Equal("acme order");
        }

        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task PooledInstance_ReusedByAnotherTenant_RejectsForgedWrites()
    {
        await using var services = BuildServices(pooledFactory: false);
        var tenants = services.GetRequiredService<ITenantContextSetter<string>>();
        int acmeOrderId;

        using (tenants.Use(Tenant("acme")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();
            await db.Database.EnsureCreatedAsync();
            Order order = new() { Description = "acme order" };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            acmeOrderId = order.Id;
        }

        using (tenants.Use(Tenant("globex")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();
            db.Orders.Update(new Order { Id = acmeOrderId, TenantId = "globex", Description = "overwritten" });

            Func<Task> save = () => db.SaveChangesAsync();
            Func<Task> moveRows = () => db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.TenantId, "acme"));

            await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
            await moveRows.Should().ThrowAsync<TenantIsolationViolationException>();
        }

        Rows().Should().BeEquivalentTo([("acme", "acme order")]);
    }

    [Fact]
    public async Task PooledFactory_ConcurrentTenants_EachSeeOnlyTheirOwnRows()
    {
        await using var services = BuildServices(pooledFactory: true);
        var tenants = services.GetRequiredService<ITenantContextSetter<string>>();
        var factory = services.GetRequiredService<IDbContextFactory<PooledOrdersContext>>();

        using (tenants.Use(Tenant("setup")))
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var tenantIds = Enumerable.Range(1, 16).Select(i => $"tenant-{i}").ToArray();

        var seen = await Task.WhenAll(tenantIds.Select(tenantId => Task.Run(async () =>
        {
            using var _ = tenants.Use(Tenant(tenantId));

            for (var i = 0; i < 3; i++)
            {
                await using var db = await factory.CreateDbContextAsync();
                db.Orders.Add(new Order { Description = $"{tenantId} order {i}" });
                await db.SaveChangesAsync();
            }

            await using var reader = await factory.CreateDbContextAsync();
            return (Tenant: tenantId, Owners: await reader.Orders.Select(o => o.TenantId).Distinct().ToListAsync(),
                Count: await reader.Orders.CountAsync());
        })));

        seen.Should().AllSatisfy(result =>
        {
            result.Owners.Should().Equal(result.Tenant);
            result.Count.Should().Be(3);
        });
        Rows().Should().HaveCount(tenantIds.Length * 3);
    }

    [Fact]
    public async Task ContextWithoutAnApplicationServiceProvider_BuildsItsModel_ButExplainsWhyItCannotQuery()
    {
        var options = new DbContextOptionsBuilder<PooledOrdersContext>().UseSqlite(_connectionString).UseTenantry().Options;
        await using var db = new PooledOrdersContext(options);

        // Design-time tools build the model without the application's services.
        db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.TenantId))!.IsConcurrencyToken.Should().BeTrue();
        await db.Awaiting(d => d.Orders.ToListAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*no application service provider*AddDbContext*UseApplicationServiceProvider*");
    }

    [Fact]
    public async Task ContextWithoutTenantry_ExplainsHowToRegisterIt()
    {
        ServiceCollection collection = new();
        collection.AddDbContext<PooledOrdersContext>(options => options.UseSqlite(_connectionString).UseTenantry());
        await using var services = collection.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();

        // Building the model throws, and so does every query and save, as EF Core may already have this context
        // type's model from another application in the process (another test here).
        await db.Awaiting(context => context.Orders.ToListAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ITenantEntity<String>*not registered*AddTenantry<String>*");
        db.Orders.Add(new Order { Description = "unisolated" });
        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ITenantEntity<String>*not registered*AddTenantry<String>*");
    }

    private ServiceProvider BuildServices(bool pooledFactory)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>();

        if (pooledFactory)
        {
            services.AddPooledDbContextFactory<PooledOrdersContext>(options => options.UseSqlite(_connectionString).UseTenantry());
        }
        else
        {
            services.AddDbContextPool<PooledOrdersContext>(options => options.UseSqlite(_connectionString).UseTenantry());
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static TenantDescriptor<string> Tenant(string id) => new() { TenantId = id, Name = id };

    private List<(string TenantId, string Description)> Rows()
    {
        using var command = _keepAlive.CreateCommand();
        command.CommandText = "SELECT TenantId, Description FROM Orders ORDER BY Id";
        using var reader = command.ExecuteReader();
        List<(string, string)> rows = [];

        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }
}
