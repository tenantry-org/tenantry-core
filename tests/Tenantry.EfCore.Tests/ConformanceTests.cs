using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenantry.Core;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;
using Tenantry.Tests.Shared;

namespace Tenantry.EfCore.Tests;

/// <summary>
/// Every Tenantry.EfCore registration, resolved in a host that validates it (see <see cref="Conformance"/>), with
/// each way of wiring a context saving and reading one tenant's row.
/// </summary>
public sealed class ConformanceTests : IDisposable
{
    // One database per context, so each context's EnsureCreated creates its own tables.
    private readonly string _intercepted = Database("intercepted");
    private readonly string _selfWiring = Database("self-wiring");
    private readonly string _acme = Database("acme");
    private readonly List<SqliteConnection> _keepAlive = [];

    public ConformanceTests()
    {
        // A named shared-cache in-memory database lives while one connection to it is open.
        foreach (var database in new[] { _intercepted, _selfWiring, _acme })
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
        builder.Services.AddTenantryCore<string>(tenant =>
        {
            tenant.UseStore<ScopedTenantStore>();
            tenant.UseConnectionStrings(options => options.GetConnectionString = _ => _acme);
            tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
        });
        builder.Services.AddDbContext<InterceptedContext>((sp, options) => options.UseSqlite(_intercepted).AddTenantInterceptors(sp));
        builder.Services.AddDbContext<SelfWiringContext>(options => options.UseSqlite(_selfWiring));
        builder.Services.AddTenantDbContextPool<PerTenantContext, string>((sp, options) => options.UseSqlite().AddTenantInterceptors(sp));

        using var host = builder.Build();

        await host.Services.GetRequiredService<ITenantScopeFactory<string>>().RunInScopeAsync("acme", async (scope, ct) =>
        {
            Conformance.ResolveEveryTenantryService(builder.Services, scope.ServiceProvider);

            foreach (var db in new IOrders[]
                     {
                         scope.ServiceProvider.GetRequiredService<InterceptedContext>(),
                         scope.ServiceProvider.GetRequiredService<SelfWiringContext>(),
                         scope.ServiceProvider.GetRequiredService<PerTenantContext>(),
                     })
            {
                var context = (DbContext)db;
                await context.Database.EnsureCreatedAsync(ct);
                db.Orders.Add(new Order { Description = context.GetType().Name });
                await context.SaveChangesAsync(ct);

                (await db.Orders.AsNoTracking().SingleAsync(ct)).TenantId.Should().Be("acme", context.GetType().Name);
            }
        });
        await Conformance.StartAndStopAsync(host);
    }

    private interface IOrders
    {
        DbSet<Order> Orders { get; }
    }

    private sealed class InterceptedContext(DbContextOptions<InterceptedContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>, IOrders
    {
        public DbSet<Order> Orders => Set<Order>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyTenantFilters<string, InterceptedContext>(this);
    }

    private sealed class SelfWiringContext(DbContextOptions<SelfWiringContext> options)
        : MultiTenantDbContext<string>(options), IOrders
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class PerTenantContext(DbContextOptions<PerTenantContext> options)
        : MultiTenantDbContext<string>(options), IOrders
    {
        public DbSet<Order> Orders => Set<Order>();
    }
}
