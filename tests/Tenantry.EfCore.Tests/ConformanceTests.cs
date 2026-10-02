using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenantry;
using Tenantry.Tests.Shared;

namespace Tenantry.EfCore.Tests;

/// <summary>
/// Every Tenantry.EfCore registration, resolved in a host that validates it (see <see cref="Conformance"/>), with
/// each way of registering a context that uses <c>UseTenantry()</c> saving and reading one tenant's row.
/// </summary>
public sealed class ConformanceTests : IDisposable
{
    // A database per context, except that both per-tenant contexts use acme's, each with its own table.
    private readonly string _scoped = Database("scoped");
    private readonly string _pooled = Database("pooled");
    private readonly string _acme = Database("acme");
    private readonly List<SqliteConnection> _keepAlive = [];

    public ConformanceTests()
    {
        // A named shared-cache in-memory database lives while one connection to it is open.
        foreach (var database in new[] { _scoped, _pooled, _acme })
        {
            SqliteConnection connection = new(database);
            connection.Open();
            _keepAlive.Add(connection);
        }
    }

    public void Dispose() => _keepAlive.ForEach(connection => connection.Dispose());

    private static string Database(string name) => $"DataSource=conformance-{Guid.NewGuid():N}-{name};Mode=Memory;Cache=Shared";

    [Fact]
    public async Task EveryEfCoreService_Resolves_AndEachContextIsolatesTheTenant()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureContainer(new DefaultServiceProviderFactory(Conformance.ProviderOptions));
        builder.Services.AddScoped<ScopedTenantStore.Session>();
        builder.Services.AddTenantry<string>(tenant =>
        {
            tenant.UseStore<ScopedTenantStore>();
            tenant.UseConnectionStrings(options => options.GetConnectionString = _ => _acme);
            tenant.ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Reject);
            tenant.AddDbContextPerTenantDatabase<PerTenantContext>((_, options) => options.UseSqlite());
            tenant.AddDbContextPerTenantDatabase<PooledPerTenantContext>((_, options) => options.UseSqlite(), pooled: true);
        });
        builder.Services.AddDbContext<ScopedContext>(options => options.UseSqlite(_scoped).UseTenantry());
        builder.Services.AddDbContextPool<PooledContext>(options => options.UseSqlite(_pooled).UseTenantry());

        using var host = builder.Build();

        await host.Services.GetRequiredService<ITenantScopeFactory<string>>().RunInScopeAsync("acme", async (scope, ct) =>
        {
            Conformance.ResolveEveryTenantryService(builder.Services, scope.ServiceProvider);

            foreach (var db in new IOrders[]
                     {
                         scope.ServiceProvider.GetRequiredService<ScopedContext>(),
                         scope.ServiceProvider.GetRequiredService<PooledContext>(),
                         scope.ServiceProvider.GetRequiredService<PerTenantContext>(),
                         scope.ServiceProvider.GetRequiredService<PooledPerTenantContext>(),
                     })
            {
                var context = (DbContext)db;
                await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
                db.Orders.Add(new Order { Description = context.GetType().Name });
                await context.SaveChangesAsync(ct);

                (await db.Orders.AsNoTracking().SingleAsync(ct)).TenantId.Should().Be("acme", context.GetType().Name);
            }
        }, TestContext.Current.CancellationToken);
        await Conformance.StartAndStopAsync(host);
    }

    private interface IOrders
    {
        DbSet<Order> Orders { get; }
    }

    private sealed class ScopedContext(DbContextOptions<ScopedContext> options) : DbContext(options), IOrders
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class PooledContext(DbContextOptions<PooledContext> options) : DbContext(options), IOrders
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class PerTenantContext(DbContextOptions<PerTenantContext> options) : DbContext(options), IOrders
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class PooledPerTenantContext(DbContextOptions<PooledPerTenantContext> options) : DbContext(options), IOrders
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>().ToTable("PooledOrders");
    }
}
