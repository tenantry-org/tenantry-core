using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tenantry.EfCore.Tests;

/// <summary>
/// <see cref="TenantModel"/>, the shared-across-tenants marker, and <see cref="TenantContextGuard"/>: the public seams
/// packages that build on Tenantry use to check a model and to fail closed.
/// </summary>
public sealed class TenantModelTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task TheModel_SaysWhichEntityTypesAreTenantOwned_SharedOrNeither()
    {
        await using var db = await DbContextFactory.CreateContextAsync(TestTenantContext.For("acme"), _connection);
        await using MarkedContext marked = new(DbContextFactory.Options<MarkedContext>(TestTenantContext.For("acme"), _connection));
        await using UnmarkedContext unmarked = new(DbContextFactory.Options<UnmarkedContext>(TestTenantContext.For("acme"), _connection));

        TenantModel.HasTenantOwnedEntityTypes(db.Model).Should().BeTrue();
        TenantModel.IsTenantOwned(db.Model.FindEntityType(typeof(Order))!).Should().BeTrue();
        TenantModel.FindUnisolatedEntityTypes(unmarked.Model).Select(e => e.ClrType).Should().Equal(typeof(Unmarked));

        TenantModel.FindUnisolatedEntityTypes(marked.Model).Should().BeEmpty();
        TenantModel.IsSharedAcrossTenants(marked.Model.FindEntityType(typeof(Country))!).Should().BeTrue();
        TenantModel.IsSharedAcrossTenants(marked.Model.FindEntityType(typeof(Unmarked))!).Should().BeTrue();
        TenantModel.HasTenantOwnedEntityTypes(marked.Model).Should().BeTrue();
    }

    [Fact]
    public async Task ATenantOwnedEntityMarkedShared_FailsTheModelCheck()
    {
        await using ContradictoryContext db = new(DbContextFactory.Options<ContradictoryContext>(TestTenantContext.For("acme"), _connection));

        await db.Awaiting(context => context.Orders.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>()
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration && e.Message.Contains("shared"));
    }

    [Fact]
    public async Task AGuard_ChecksTheContextBeforeItQueriesOrSaves()
    {
        RefusingGuard guard = new();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(DbContextFactory.Services(TestTenantContext.For("acme")))
            .UseTenantry()
            .AddInterceptors(guard)
            .Options;

        await using TestDbContext db = new(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        guard.Refuse = true;

        await db.Awaiting(context => context.Orders.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().Where(e => e.Kind == TenantIsolationViolationKind.TenantSchemaMismatch);

        db.Orders.Add(new Order { Description = "x" });
        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        guard.Checks.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ASaveAGuardRefuses_LeavesItsNewEntitiesUnstamped_ThoughTheGuardWasAddedAfterTenantry()
    {
        RefusingGuard guard = new();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(DbContextFactory.Services(TestTenantContext.For("acme")))
            .UseTenantry()
            .AddInterceptors(guard)
            .Options;

        await using TestDbContext db = new(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        Order order = new() { Description = "Refused" };
        db.Orders.Add(order);
        guard.Refuse = true;

        db.Invoking(context => context.SaveChanges()).Should().Throw<TenantIsolationViolationException>();
        await db.Awaiting(context => context.SaveChangesAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TenantIsolationViolationException>();
        order.TenantId.Should().BeEmpty();
    }

    private sealed class RefusingGuard : TenantContextGuard
    {
        public bool Refuse { get; set; }

        public int Checks { get; private set; }

        protected override void Check(DbContext context)
        {
            Checks++;

            if (Refuse)
            {
                throw new TenantIsolationViolationException(
                    TenantIsolationViolationKind.TenantSchemaMismatch, context.GetType().Name, "Refused by the test.");
            }
        }
    }

    [SharedAcrossTenants]
    public sealed class Country
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class MarkedContext(DbContextOptions<MarkedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>();
            modelBuilder.Entity<Country>();
            modelBuilder.Entity<Unmarked>().IsSharedAcrossTenants();
        }
    }

    public sealed class Unmarked
    {
        public int Id { get; set; }
    }

    public sealed class UnmarkedContext(DbContextOptions<UnmarkedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>();
            modelBuilder.Entity<Unmarked>();
        }
    }

    public sealed class ContradictoryContext(DbContextOptions<ContradictoryContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>().IsSharedAcrossTenants();
    }
}
