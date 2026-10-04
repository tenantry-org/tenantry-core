using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Tenantry;

namespace Tenantry.EfCore.Tests.ModelBuilding;

/// <summary>
/// A model with tenant-owned entity types must classify every other entity type as shared across tenants, unless
/// <see cref="EfCoreIsolationOptions.OnUnclassifiedEntityType"/> says otherwise. A model without tenant-owned entity
/// types is never checked.
/// </summary>
public sealed class UnclassifiedEntityTypeTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.For("acme");

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task UnclassifiedEntityTypes_FailTheFirstQueryAndSave_NamingEachRoot()
    {
        await using var db = await CreateAsync<UnclassifiedContext>();

        var query = await db.Awaiting(context => context.Orders.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        query.Which.Kind.Should().Be(TenantIsolationViolationKind.ModelConfiguration);
        query.Which.TypeName.Should().Be(nameof(UnclassifiedContext));
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
    public async Task EveryShapeOfClassifiedEntityType_IsAccepted()
    {
        await using var db = await CreateAsync<ClassifiedContext>();

        (await db.Orders.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        TenantModel.FindUnisolatedEntityTypes(db.Model).Should().BeEmpty();
        TenantModel.IsSharedAcrossTenants(db.Model.FindEntityType(typeof(Dog))!).Should().BeTrue("its root is marked");
        TenantModel.IsSharedAcrossTenants(db.Model.FindEntityType(typeof(Engine))!).Should().BeTrue("its owner is marked");
    }

    [Fact]
    public async Task AJoinEntityWithDataOfItsOwn_IsClassifiedLikeAnyOther_AndAPureOneFollowsTheTypesItJoins()
    {
        await using var db = await CreateAsync<JoinsContext>();

        TenantModel.FindUnisolatedEntityTypes(db.Model).Select(type => type.ClrType).Should().Equal(typeof(GroupRole));
        await db.Awaiting(context => context.Set<GroupRole>().CountAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().Where(e => e.Message.Contains("GroupRole"));
    }

    [Fact]
    public async Task AValueOutsideTheEnum_IsTreatedAsReject()
    {
        await using var db = await CreateAsync<UnclassifiedContext>(
            new EfCoreIsolationOptions { OnUnclassifiedEntityType = (UnclassifiedEntityTypeBehavior)7 });

        await db.Awaiting(context => context.Set<Invoice>().CountAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        db.Set<Invoice>().Add(new Invoice());
        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }

    [Theory]
    [InlineData(UnclassifiedEntityTypeBehavior.Warn)]
    [InlineData(UnclassifiedEntityTypeBehavior.Allow)]
    public async Task UnderWarnOrAllow_TheModelIsUsed(UnclassifiedEntityTypeBehavior behavior)
    {
        await using var db = await CreateAsync<UnclassifiedContext>(new EfCoreIsolationOptions { OnUnclassifiedEntityType = behavior });

        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.Orders.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task ContextsSharingAModel_EachFollowTheirOwnOptions()
    {
        // One application, one context type: a maintenance context allows the model, the application's does not.
        var services = DbContextFactory.Services<string>(_tenant);
        await using UnclassifiedContext allowed = new(new DbContextOptionsBuilder<UnclassifiedContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry(o => o.OnUnclassifiedEntityType = UnclassifiedEntityTypeBehavior.Allow)
            .Options);
        await using UnclassifiedContext rejected = new(new DbContextOptionsBuilder<UnclassifiedContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry()
            .Options);

        await RunEachAsync(allowed, rejected);
    }

    [Fact]
    public async Task ContextsOfApplicationsWithDifferentOptions_EachFollowTheirOwn()
    {
        // Two hosts in one process, as in tests, whose contexts use the options of their own application.
        await using UnclassifiedContext allowed = new(DbContextFactory.Options<UnclassifiedContext>(
            _tenant, _connection, new EfCoreIsolationOptions { OnUnclassifiedEntityType = UnclassifiedEntityTypeBehavior.Allow }));
        await using UnclassifiedContext rejected = new(DbContextFactory.Options<UnclassifiedContext>(_tenant, _connection));

        await RunEachAsync(allowed, rejected);
    }

    // The allowed context runs a query, a compiled query and a save first, so a cache shared with it would let the
    // rejected context's run them unchecked.
    private static async Task RunEachAsync(UnclassifiedContext allowed, UnclassifiedContext rejected)
    {
        var ct = TestContext.Current.CancellationToken;
        var countInvoices = EF.CompileAsyncQuery((UnclassifiedContext context) => context.Set<Invoice>().Count());
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
        await rejected.Awaiting(context => EF.CompileAsyncQuery((UnclassifiedContext c) => c.Set<Invoice>().Count())(context))
            .Should().ThrowAsync<TenantIsolationViolationException>();
        rejected.Set<Invoice>().Add(new Invoice());
        await rejected.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
    }

    [Fact]
    public async Task AModelWithoutTenantOwnedEntityTypes_IsNotChecked()
    {
        await using var db = await CreateAsync<NoTenantOwnedTypesContext>();

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

    private sealed class UnclassifiedContext(DbContextOptions<UnclassifiedContext> options) : DbContext(options)
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

    private sealed class ClassifiedContext(DbContextOptions<ClassifiedContext> options) : DbContext(options)
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
