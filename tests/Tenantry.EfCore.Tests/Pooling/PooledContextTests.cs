using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;

namespace Tenantry.EfCore.Tests.Pooling;

/// <summary>A pool-compatible context: its only constructor takes the options.</summary>
public sealed class PooledOrdersContext(DbContextOptions<PooledOrdersContext> options)
    : MultiTenantDbContext<string>(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>
/// A pool-compatible context that does not derive from <see cref="MultiTenantDbContext{TKey}"/>: it resolves the
/// ambient tenant context through <c>GetService</c> instead of its constructor.
/// </summary>
public sealed class RawPooledOrdersContext(DbContextOptions<RawPooledOrdersContext> options)
    : DbContext(options), ITenantAwareDbContext<string>
{
    private ITenantContext<string>? _tenantContext;

    public DbSet<Order> Orders => Set<Order>();

    public string? CurrentTenantId => (_tenantContext ??= this.GetService<ITenantContext<string>>()).CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyTenantFilters<string, RawPooledOrdersContext>(this);
}

/// <summary>
/// Verifies that pooled <see cref="MultiTenantDbContext{TKey}"/> instances isolate whichever tenant is active
/// each time they are used, through <c>AddDbContextPool</c> and <c>AddPooledDbContextFactory</c>.
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
        var tenants = services.GetRequiredService<ITenantScope<string>>();
        Guid instanceId;

        using (tenants.BeginScope(Tenant("acme")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();
            await db.Database.EnsureCreatedAsync();
            db.Orders.Add(new Order { Description = "acme order" });
            await db.SaveChangesAsync();
            instanceId = db.ContextId.InstanceId;
        }

        using (tenants.BeginScope(Tenant("globex")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();

            db.ContextId.InstanceId.Should().Be(instanceId, "the pooled instance is reused");
            (await db.Orders.CountAsync()).Should().Be(0);

            db.Orders.Add(new Order { Description = "globex order" });
            await db.SaveChangesAsync();
        }

        using (tenants.BeginScope(Tenant("acme")))
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
        var tenants = services.GetRequiredService<ITenantScope<string>>();
        int acmeOrderId;

        using (tenants.BeginScope(Tenant("acme")))
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();
            await db.Database.EnsureCreatedAsync();
            Order order = new() { Description = "acme order" };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            acmeOrderId = order.Id;
        }

        using (tenants.BeginScope(Tenant("globex")))
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
        var tenants = services.GetRequiredService<ITenantScope<string>>();
        var factory = services.GetRequiredService<IDbContextFactory<PooledOrdersContext>>();

        using (tenants.BeginScope(Tenant("setup")))
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var tenantIds = Enumerable.Range(1, 16).Select(i => $"tenant-{i}").ToArray();

        var seen = await Task.WhenAll(tenantIds.Select(tenantId => Task.Run(async () =>
        {
            using var _ = tenants.BeginScope(Tenant(tenantId));

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
    public async Task RawPooledContext_ResolvingTenantContextThroughGetService_IsolatesEachTenant()
    {
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddTenantryCore<string>(tenant => tenant.AddEfCoreIsolation());
        collection.AddDbContextPool<RawPooledOrdersContext>((sp, options) =>
            options.UseSqlite(_connectionString).AddTenantInterceptors(sp));
        await using var services = collection.BuildServiceProvider();
        var tenants = services.GetRequiredService<ITenantScope<string>>();

        foreach (var tenantId in new[] { "acme", "globex" })
        {
            using (tenants.BeginScope(Tenant(tenantId)))
            await using (var scope = services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RawPooledOrdersContext>();
                await db.Database.EnsureCreatedAsync();
                db.Orders.Add(new Order { Description = $"{tenantId} order" });
                await db.SaveChangesAsync();

                (await db.Orders.Select(o => o.TenantId).ToListAsync()).Should().Equal(tenantId);
            }
        }

        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task PooledContext_WithoutTenantInterceptors_FailsInsteadOfSavingUnisolated()
    {
        await using var services = BuildServices(pooledFactory: false, addTenantInterceptors: false);
        var tenants = services.GetRequiredService<ITenantScope<string>>();

        using (tenants.BeginScope(Tenant("acme")))
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .Awaiting(sp => sp.GetRequiredService<PooledOrdersContext>().Database.EnsureCreatedAsync())
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*OnConfiguring*pooling*");
        }
    }

    [Fact]
    public void ContextWithoutTenantContext_ExplainsHowToRegisterTenantry()
    {
        var options = new DbContextOptionsBuilder<PooledOrdersContext>().UseSqlite(_connectionString).Options;
        using var db = new PooledOrdersContext(options);

        db.Invoking(d => d.CurrentTenantId)
            .Should().Throw<InvalidOperationException>().WithMessage("*ITenantContext*AddTenantry*");
    }

    private ServiceProvider BuildServices(bool pooledFactory, bool addTenantInterceptors = true)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant => tenant.AddEfCoreIsolation());

        void Configure(IServiceProvider sp, DbContextOptionsBuilder options)
        {
            options.UseSqlite(_connectionString);

            if (addTenantInterceptors)
            {
                options.AddTenantInterceptors(sp);
            }
        }

        if (pooledFactory)
        {
            services.AddPooledDbContextFactory<PooledOrdersContext>(Configure);
        }
        else
        {
            services.AddDbContextPool<PooledOrdersContext>(Configure);
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
