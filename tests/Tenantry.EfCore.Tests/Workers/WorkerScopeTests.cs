using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.EfCore.Tests.Pooling;

namespace Tenantry.EfCore.Tests.Workers;

/// <summary>
/// Background-worker patterns end to end: <see cref="ITenantScopeFactory{TKey}"/> scopes around EF Core work,
/// with both a regular and a pooled <see cref="MultiTenantDbContext{TKey}"/>.
/// </summary>
public sealed class WorkerScopeTests : IDisposable
{
    private static readonly TenantDescriptor<string>[] Tenants =
    [
        new() { TenantId = "acme", Name = "Acme" },
        new() { TenantId = "globex", Name = "Globex" },
    ];

    private readonly string _connectionString = $"DataSource=workers-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public WorkerScopeTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public void Dispose() => _keepAlive.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SweepOverEveryTenant_StampsAndIsolatesEachTenantsWrites(bool pooled)
    {
        await using var services = BuildServices(pooled);
        await CreateSchemaAsync(services);
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();

        foreach (var tenant in await services.GetRequiredService<ITenantStoreAccessor<string>>().GetAllTenantsAsync())
        {
            await using var scope = scopes.CreateScope(tenant);
            await using var db = Orders(scope, pooled);
            db.Orders.Add(new Order { Description = $"{tenant.TenantId} sweep" });
            await db.SaveChangesAsync();
        }

        (await ReadAsAsync(services, "acme", pooled)).Should().Equal("acme:acme sweep");
        (await ReadAsAsync(services, "globex", pooled)).Should().Equal("globex:globex sweep");
        services.GetRequiredService<ITenantContext<string>>().HasTenant.Should().BeFalse("the sweep leaves no tenant behind");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRunsByTenantId_EachWriteOnlyTheirOwnTenant(bool pooled)
    {
        await using var services = BuildServices(pooled);
        await CreateSchemaAsync(services);
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();

        await Task.WhenAll(Tenants.Select(tenant => scopes.RunInScopeAsync(tenant.TenantId, async (scope, ct) =>
        {
            await using var db = Orders(scope, pooled);

            for (var i = 0; i < 5; i++)
            {
                db.Orders.Add(new Order { Description = $"{tenant.TenantId} {i}" });
                await db.SaveChangesAsync(ct);
                await Task.Yield();
            }
        })));

        (await ReadAsAsync(services, "acme", pooled)).Should().HaveCount(5).And.OnlyContain(row => row.StartsWith("acme:acme"));
        (await ReadAsAsync(services, "globex", pooled)).Should().HaveCount(5).And.OnlyContain(row => row.StartsWith("globex:globex"));
    }

    [Fact]
    public async Task WriteAfterTheSweep_HasNoTenantAndIsRejected()
    {
        await using var services = BuildServices(pooled: false);
        await CreateSchemaAsync(services);
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();

        foreach (var tenant in Tenants)
        {
            await using var scope = scopes.CreateScope(tenant);
            await scope.ServiceProvider.GetRequiredService<PooledOrdersContext>().Orders.CountAsync();
        }

        // The old Pro scope left the last tenant active here, so this write would have been stamped "globex".
        await using var unscoped = services.CreateAsyncScope();
        var db = unscoped.ServiceProvider.GetRequiredService<PooledOrdersContext>();
        db.Orders.Add(new Order { Description = "after the sweep" });

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantNotResolvedException>();
    }

    private ServiceProvider BuildServices(bool pooled)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseInMemoryStore(Tenants);
            tenant.AddEfCoreIsolation();
        });

        void Configure(IServiceProvider sp, DbContextOptionsBuilder options) =>
            options.UseSqlite(_connectionString).AddTenantInterceptors(sp);

        if (pooled)
        {
            services.AddPooledDbContextFactory<PooledOrdersContext>(Configure);
        }
        else
        {
            services.AddDbContext<PooledOrdersContext>(Configure);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // A pooled factory hands out a context per call, returned to the pool when disposed. Disposing the scope's own
    // context early is harmless, so callers always dispose.
    private static PooledOrdersContext Orders(ITenantScope<string> scope, bool pooled) =>
        pooled
            ? scope.ServiceProvider.GetRequiredService<IDbContextFactory<PooledOrdersContext>>().CreateDbContext()
            : scope.ServiceProvider.GetRequiredService<PooledOrdersContext>();

    private static async Task CreateSchemaAsync(ServiceProvider services)
    {
        await using var scope = services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(Tenants[0]);
        await using var db = Orders(scope, pooled: services.GetService<IDbContextFactory<PooledOrdersContext>>() is not null);
        await db.Database.EnsureCreatedAsync();
    }

    private static Task<List<string>> ReadAsAsync(ServiceProvider services, string tenantId, bool pooled) =>
        services.GetRequiredService<ITenantScopeFactory<string>>().RunInScopeAsync(tenantId, async (scope, ct) =>
        {
            await using var db = Orders(scope, pooled);
            return await db.Orders
                .OrderBy(order => order.Id)
                .Select(order => order.TenantId + ":" + order.Description)
                .ToListAsync(ct);
        });
}
