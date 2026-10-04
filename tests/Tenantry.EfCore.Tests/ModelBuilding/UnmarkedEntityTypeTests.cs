using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;

namespace Tenantry.EfCore.Tests.ModelBuilding;

/// <summary>
/// An entity type that is not tenant-owned is shared by every tenant. An application that sets
/// <see cref="EfCoreIsolationOptions.OnUnmarkedEntityType"/> to <c>Warn</c> or <c>Reject</c> wants each such type
/// marked as shared, in a model that has tenant-owned entity types. A model without them is never checked.
/// </summary>
public sealed class UnmarkedEntityTypeTests : IDisposable
{
    private static readonly EfCoreIsolationOptions Reject = new() { OnUnmarkedEntityType = UnmarkedEntityTypeBehavior.Reject };

    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.For("acme");

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ByDefault_AnUnmarkedEntityType_IsSharedByEveryTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await CreateAsync<UnmarkedContext>();
        db.Set<Invoice>().Add(new Invoice());
        await db.SaveChangesAsync(ct);
        _tenant.As("globex");

        (await db.Set<Invoice>().CountAsync(ct)).Should().Be(1);
        new EfCoreIsolationOptions().OnUnmarkedEntityType.Should().Be(UnmarkedEntityTypeBehavior.Allow);
        default(UnmarkedEntityTypeBehavior).Should().Be(UnmarkedEntityTypeBehavior.Allow);
    }

    [Fact]
    public async Task UnderReject_UnmarkedEntityTypes_FailTheFirstQueryAndSave_NamingEachRoot()
    {
        await using var db = await CreateAsync<UnmarkedContext>(Reject);

        var query = await db.Awaiting(context => context.Orders.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        query.Which.Kind.Should().Be(TenantIsolationViolationKind.ModelConfiguration);
        query.Which.TypeName.Should().Be(nameof(UnmarkedContext));
        query.Which.Message.Should()
            .Contain("Invoice, InvoiceSummary, Setting (Dictionary<string, object>)")
            .And.Contain("ITenantEntity<String>")
            .And.Contain("[SharedAcrossTenants]")
            .And.Contain("IsSharedAcrossTenants()")
            .And.NotContain("CreditNote", "a derived type follows its root")
            .And.NotContain("InvoiceLine", "an owned type follows its owner");

        db.Orders.Add(new Order { Description = "Acme order" });
        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().Where(e => e.Message.Contains("Invoice"));
        await using var count = _connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Orders";
        (await count.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(0L, "nothing is written");
    }

    [Fact]
    public async Task UnderReject_EveryShapeOfMarkedEntityType_IsAccepted()
    {
        await using var db = await CreateAsync<MarkedContext>(Reject);

        (await db.Orders.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        TenantModel.FindUnisolatedEntityTypes(db.Model).Should().BeEmpty();
        TenantModel.IsSharedAcrossTenants(db.Model.FindEntityType(typeof(Dog))!).Should().BeTrue("its root is marked");
        TenantModel.IsSharedAcrossTenants(db.Model.FindEntityType(typeof(Engine))!).Should().BeTrue("its owner is marked");
    }

    [Fact]
    public async Task AJoinEntityWithDataOfItsOwn_IsCheckedLikeAnyOther_AndAPureOneFollowsTheTypesItJoins()
    {
        await using var db = await CreateAsync<JoinsContext>(Reject);

        TenantModel.FindUnisolatedEntityTypes(db.Model).Select(type => type.ClrType).Should().Equal(typeof(GroupRole));
        await db.Awaiting(context => context.Set<GroupRole>().CountAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().Where(e => e.Message.Contains("GroupRole"));
    }

    [Fact]
    public async Task AValueOutsideTheEnum_IsTreatedAsReject()
    {
        await using var db = await CreateAsync<UnmarkedContext>(
            new EfCoreIsolationOptions { OnUnmarkedEntityType = (UnmarkedEntityTypeBehavior)7 });

        await db.Awaiting(context => context.Set<Invoice>().CountAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        db.Set<Invoice>().Add(new Invoice());
        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }

    [Theory]
    [InlineData(UnmarkedEntityTypeBehavior.Warn)]
    [InlineData(UnmarkedEntityTypeBehavior.Allow)]
    public async Task UnderWarnOrAllow_TheModelIsUsed(UnmarkedEntityTypeBehavior behavior)
    {
        await using var db = await CreateAsync<UnmarkedContext>(new EfCoreIsolationOptions { OnUnmarkedEntityType = behavior });

        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.Orders.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task ContextsOfOneApplication_EachFollowTheirOwnOptions()
    {
        // One application, one context type: the application's contexts use the default, one context rejects.
        var services = DbContextFactory.Services<string>(_tenant);
        await using UnmarkedContext allowed = new(new DbContextOptionsBuilder<UnmarkedContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry()
            .Options);
        await using UnmarkedContext rejected = new(new DbContextOptionsBuilder<UnmarkedContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry(o => o.OnUnmarkedEntityType = UnmarkedEntityTypeBehavior.Reject)
            .Options);

        await RunEachAsync(allowed, rejected);
    }

    [Fact]
    public async Task ContextsOfApplicationsWithDifferentOptions_EachFollowTheirOwn()
    {
        // Two hosts in one process, as in tests, whose contexts use the options of their own application.
        await using UnmarkedContext allowed = new(DbContextFactory.Options<UnmarkedContext>(_tenant, _connection));
        await using UnmarkedContext rejected = new(DbContextFactory.Options<UnmarkedContext>(_tenant, _connection, Reject));

        await RunEachAsync(allowed, rejected);
    }

    [Theory]
    [InlineData(UnmarkedEntityTypeBehavior.Reject)]
    [InlineData(UnmarkedEntityTypeBehavior.Warn)]
    public async Task OptionsThatGetTheirServicesAfterUseTenantry_WorkUnderAllow_AndRefuseAnApplicationThatChecks(
        UnmarkedEntityTypeBehavior checking)
    {
        // UseTenantry() runs before the options have the application's services, so it reads neither application's
        // options, and the two contexts share EF Core's caches.
        DbContextOptions<UnmarkedContext> Options(EfCoreIsolationOptions? isolation) =>
            new DbContextOptionsBuilder<UnmarkedContext>()
                .UseSqlite(_connection)
                .UseTenantry()
                .UseApplicationServiceProvider(DbContextFactory.Services<string>(_tenant, isolation))
                .Options;
        var ct = TestContext.Current.CancellationToken;
        var countInvoices = EF.CompileAsyncQuery((UnmarkedContext context) => context.Set<Invoice>().Count());

        await using UnmarkedContext allowed = new(Options(null));
        await allowed.Database.EnsureCreatedAsync(ct);
        allowed.Set<Invoice>().Add(new Invoice());
        await allowed.SaveChangesAsync(ct);
        (await allowed.Set<Invoice>().CountAsync(ct)).Should().Be(1);
        (await countInvoices(allowed)).Should().Be(1);

        // The same query and compiled query come from EF Core's cache, compiled for the allowed context: their
        // commands are refused.
        await using UnmarkedContext refused = new(Options(new EfCoreIsolationOptions { OnUnmarkedEntityType = checking }));
        await refused.Awaiting(context => context.Set<Invoice>().CountAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseApplicationServiceProvider before UseTenantry()*");
        await refused.Awaiting(context => countInvoices(context))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseApplicationServiceProvider before UseTenantry()*");
        refused.Set<Invoice>().Add(new Invoice());
        await refused.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseApplicationServiceProvider before UseTenantry()*");
    }

    [Fact]
    public void ManyApplicationsInOneProcess_ShareOneEfCoreProviderPerOptionValue()
    {
        // EF Core throws once more than twenty internal service providers exist, so 25 hosts must not each make one.
        List<IMemoryCache> providers = [];

        for (var i = 0; i < 25; i++)
        {
            ServiceCollection services = new();
            services.AddSingleton<ITenantContext<string>>(_tenant);
            services.AddTenantry<string>(tenant => tenant.ConfigureEfCoreIsolation(o =>
                o.OnUnmarkedEntityType = i % 2 == 0 ? UnmarkedEntityTypeBehavior.Allow : UnmarkedEntityTypeBehavior.Reject));
            services.AddDbContext<UnmarkedContext>(options => options.UseSqlite(_connection).UseTenantry());
            var application = services.BuildServiceProvider();
            using var scope = application.CreateScope();
            // A singleton of EF Core's internal service provider stands for the provider.
            providers.Add(scope.ServiceProvider.GetRequiredService<UnmarkedContext>().GetService<IMemoryCache>());
        }

        providers.Distinct().Should().HaveCount(2);
    }

    // The allowed context runs a query, a compiled query and a save first, so a cache shared with it would let the
    // rejected context's run them unchecked.
    private static async Task RunEachAsync(UnmarkedContext allowed, UnmarkedContext rejected)
    {
        var ct = TestContext.Current.CancellationToken;
        var countInvoices = EF.CompileAsyncQuery((UnmarkedContext context) => context.Set<Invoice>().Count());
        await allowed.Database.EnsureCreatedAsync(ct);
        allowed.Set<Invoice>().Add(new Invoice());
        await allowed.SaveChangesAsync(ct);
        (await allowed.Set<Invoice>().CountAsync(ct)).Should().Be(1);
        (await countInvoices(allowed)).Should().Be(1);

        await rejected.Awaiting(context => context.Set<Invoice>().CountAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();

        // The rejected context has a model of its own, so EF Core refuses the allowed context's compiled query, and
        // compiling it again for the rejected context's model runs the check.
        await rejected.Awaiting(context => countInvoices(context))
            .Should().ThrowAsync<InvalidOperationException>();
        await rejected.Awaiting(context => EF.CompileAsyncQuery((UnmarkedContext c) => c.Set<Invoice>().Count())(context))
            .Should().ThrowAsync<TenantIsolationViolationException>();
        rejected.Set<Invoice>().Add(new Invoice());
        await rejected.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }

    [Fact]
    public async Task AModelWithoutTenantOwnedEntityTypes_IsNotChecked()
    {
        await using var db = await CreateAsync<NoTenantOwnedTypesContext>(Reject);

        (await db.Animals.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        TenantModel.FindUnisolatedEntityTypes(db.Model).Select(type => type.ClrType).Should().Equal(typeof(Animal));
    }

    private async Task<TContext> CreateAsync<TContext>(EfCoreIsolationOptions? isolation = null)
        where TContext : DbContext
    {
        var db = (TContext)Activator.CreateInstance(typeof(TContext), DbContextFactory.Options<TContext>(_tenant, _connection, isolation))!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public class Invoice
    {
        public int Id { get; set; }

        public List<InvoiceLine> Lines { get; } = [];
    }

    public sealed class CreditNote : Invoice;

    public sealed class InvoiceLine
    {
        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    public sealed class InvoiceSummary
    {
        public int Count { get; set; }
    }

    public class Animal
    {
        public int Id { get; set; }
    }

    public sealed class Dog : Animal;

    [SharedAcrossTenants]
    public class Vehicle
    {
        public int Id { get; set; }

        public Engine Engine { get; set; } = new();
    }

    public sealed class Car : Vehicle;

    public sealed class Engine
    {
        public int Power { get; set; }
    }

    public abstract class Shape
    {
        public int Id { get; set; }
    }

    public sealed class Circle : Shape;

    [SharedAcrossTenants]
    public sealed class Tag
    {
        public int Id { get; set; }

        public List<Label> Labels { get; } = [];
    }

    [SharedAcrossTenants]
    public sealed class Label
    {
        public int Id { get; set; }

        public List<Tag> Tags { get; } = [];
    }

    public sealed class Basket : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<BasketItem> Items { get; } = [];

        public BasketItem? Featured { get; set; }
    }

    public sealed class BasketItem
    {
        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
    }

    [SharedAcrossTenants]
    public sealed class Group
    {
        public int Id { get; set; }

        public List<Role> Roles { get; } = [];

        public List<Role> Members { get; } = [];
    }

    [SharedAcrossTenants]
    public sealed class Role
    {
        public int Id { get; set; }

        public List<Group> Groups { get; } = [];

        public List<Group> MemberOf { get; } = [];
    }

    // A join with data of its own, and a foreign key to a tenant-owned entity.
    public sealed class GroupRole
    {
        public int GroupId { get; set; }

        public int RoleId { get; set; }

        [MaxLength(64)]
        public string Note { get; set; } = string.Empty;

        public int? OrderId { get; set; }
    }

    // A pure join: the two foreign keys and nothing else.
    public sealed class GroupMember
    {
        public int GroupId { get; set; }

        public int RoleId { get; set; }
    }

    private sealed class JoinsContext(DbContextOptions<JoinsContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>();
            modelBuilder.Entity<Group>().HasMany(group => group.Roles).WithMany(role => role.Groups).UsingEntity<GroupRole>(
                right => right.HasOne<Role>().WithMany().HasForeignKey(join => join.RoleId),
                left => left.HasOne<Group>().WithMany().HasForeignKey(join => join.GroupId),
                join => join.HasOne<Order>().WithMany().HasForeignKey(row => row.OrderId));
            modelBuilder.Entity<Group>().HasMany(group => group.Members).WithMany(role => role.MemberOf).UsingEntity<GroupMember>(
                right => right.HasOne<Role>().WithMany().HasForeignKey(join => join.RoleId),
                left => left.HasOne<Group>().WithMany().HasForeignKey(join => join.GroupId));
        }
    }

    private sealed class UnmarkedContext(DbContextOptions<UnmarkedContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>().OwnsMany(invoice => invoice.Lines);
            modelBuilder.Entity<CreditNote>();
            modelBuilder.Entity<InvoiceSummary>().HasNoKey().ToView("InvoiceSummaries");
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("Setting", setting =>
            {
                setting.IndexerProperty<int>("Id");
                setting.IndexerProperty<string>("Value");
            });
        }
    }

    private sealed class MarkedContext(DbContextOptions<MarkedContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Owned types and owned collections of a tenant-owned type
            modelBuilder.Entity<Basket>(basket =>
            {
                basket.OwnsMany(b => b.Items);
                basket.OwnsOne(b => b.Featured);
            });

            // TPH, marked on the root in the model; TPT, marked on the root with the attribute, with an owned type
            modelBuilder.Entity<Animal>().IsSharedAcrossTenants();
            modelBuilder.Entity<Dog>();
            modelBuilder.Entity<Vehicle>().UseTptMappingStrategy().OwnsOne(vehicle => vehicle.Engine);
            modelBuilder.Entity<Car>().ToTable("Cars");

            // TPC, under an abstract root
            modelBuilder.Entity<Shape>().UseTpcMappingStrategy().IsSharedAcrossTenants().Property(s => s.Id).ValueGeneratedNever();
            modelBuilder.Entity<Circle>().ToTable("Circles");

            // Keyless and mapped to a view
            modelBuilder.Entity<InvoiceSummary>().HasNoKey().ToView("InvoiceSummaries").IsSharedAcrossTenants();

            // An implicit many-to-many join entity between shared types
            modelBuilder.Entity<Tag>().HasMany(tag => tag.Labels).WithMany(label => label.Tags);

            // A shared-type entity type
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("Setting", setting =>
            {
                setting.IndexerProperty<int>("Id");
                setting.IndexerProperty<string>("Value");
                setting.IsSharedAcrossTenants();
            });
        }
    }

    private sealed class NoTenantOwnedTypesContext(DbContextOptions<NoTenantOwnedTypesContext> options) : DbContext(options)
    {
        public DbSet<Animal> Animals => Set<Animal>();
    }
}
