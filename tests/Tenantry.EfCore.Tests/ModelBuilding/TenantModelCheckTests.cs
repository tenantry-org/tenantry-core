using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.ModelBuilding;

/// <summary>
/// The model <c>UseTenantry()</c> builds isolates every tenant-owned entity type, whatever the context configures;
/// configuration that cannot be isolated fails the model build; and a model that lost its isolation after
/// <c>UseTenantry()</c> built it fails on its first query or save.
/// </summary>
public sealed class TenantModelCheckTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.For("acme");

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task EntityTypeConfiguredAnywhereInOnModelCreating_IsFiltered()
    {
        await using var db = await CreateAsync<EntityAddedLastContext>();
        db.Set<LateItem>().Add(new LateItem());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _tenant.As("globex");

        (await db.Set<LateItem>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task SharedTypeEntityTypes_AreEachFiltered()
    {
        await using var db = await CreateAsync<SharedTypeContext>();
        db.Set<LateItem>("CurrentItems").Add(new LateItem());
        db.Set<LateItem>("ArchivedItems").Add(new LateItem());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        (await db.Set<LateItem>("ArchivedItems").CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        _tenant.As("globex");
        (await db.Set<LateItem>("CurrentItems").CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<LateItem>("ArchivedItems").CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task KeylessTenantOwnedType_IsFiltered()
    {
        await using var db = await CreateAsync<KeylessContext>();
        db.Items.Add(new Item { Name = "acme item" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _tenant.As("globex");
        db.Items.Add(new Item { Name = "globex item" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.ItemNames.Select(view => view.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Equal("globex item");
        db.Model.FindEntityType(typeof(ItemName))!.FindProperty(nameof(ItemName.TenantId))!.IsConcurrencyToken.Should().BeFalse();
    }

    [Fact]
    public async Task IntTenantKeys_AreStampedAndFiltered_ThroughTheAmbientTenant()
    {
        ServiceCollection services = new();
        services.AddTenantry<int>();
        services.AddDbContext<IntKeyContext>(options => options.UseSqlite(_connection).UseTenantry());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var tenants = provider.GetRequiredService<ITenantContextSetter<int>>();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntKeyContext>();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        using (tenants.MakeCurrent(new TenantDescriptor<int> { TenantId = 1, Name = "one" }))
        {
            db.Items.Add(new IntItem());
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (tenants.MakeCurrent(new TenantDescriptor<int> { TenantId = 2, Name = "two" }))
        {
            (await db.Items.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        }

        (await db.Items.CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0, "no tenant is current");
        (await db.Items.IgnoreQueryFilters().SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).TenantId.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrencyTokenTurnedOffInOnModelCreating_IsTurnedBackOn()
    {
        await using var db = await CreateAsync<TokenResetContext>();

        db.Model.FindEntityType(typeof(Item))!.FindProperty(nameof(Item.TenantId))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public async Task ModelContributor_RunsAfterOnModelCreating_AndItsEntityTypesAreFiltered()
    {
        await using var db = new ContributedContext(Options<ContributedContext>(services =>
            services.AddSingleton<ITenantModelContributor, LateItemContributor>()));
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Set<LateItem>().Add(new LateItem());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Model.FindEntityType(typeof(LateItem))!.GetTableName().Should().Be("ContributedItems");
        (await db.Set<LateItem>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        _tenant.As("globex");
        (await db.Set<LateItem>().CountAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplacedModelCustomizer_FailsWithAnExplanation(bool replacedFirst)
    {
        var builder = new DbContextOptionsBuilder<ItemsOnlyContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(DbContextFactory.Services(_tenant));

        if (replacedFirst)
        {
            builder.ReplaceService<IModelCustomizer, OtherModelCustomizer>().UseTenantry();
        }
        else
        {
            builder.UseTenantry().ReplaceService<IModelCustomizer, OtherModelCustomizer>();
        }

        // EF Core validates the options when the context is created.
        FluentActions.Invoking(() => new ItemsOnlyContext(builder.Options))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*replaces EF Core's IModelCustomizer with 'OtherModelCustomizer'*ITenantModelContributor*");
    }

    [Fact]
    public void ExplicitlyImplementedTenantId_FailsToBuildTheModel()
    {
        var db = new ExplicitTenantIdContext(DbContextFactory.Options<ExplicitTenantIdContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Tenant-owned entity 'ExplicitItem' has no mapped public property 'TenantId' of type String*")
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration);
    }

    [Fact]
    public void TenantOwnedOwnedTypeMappedToJson_FailsToBuildTheModel()
    {
        // Its owner's row, which carries the owner's TenantId, holds it; EF Core cannot check a TenantId of its own.
        var db = new JsonOwnedContext(DbContextFactory.Options<JsonOwnedContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'JsonLine' is tenant-owned and mapped to JSON*do not implement ITenantEntity<String> on it.")
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration);
    }

#if EFCORE10_OR_GREATER
    [Fact]
    public void OwnFilterNamedLikeTheTenantFilter_FailsToBuildTheModel()
    {
        var db = new ReservedFilterNameContext(DbContextFactory.Options<ReservedFilterNameContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("*query filter named 'Tenantry.Tenant'*Give your filter another name*");
    }
#endif

    [Fact]
    public void UseInternalServiceProvider_IsRefused()
    {
        var internalServices = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ItemsOnlyContext>()
            .UseSqlite(_connection)
            .UseInternalServiceProvider(internalServices)
            .UseApplicationServiceProvider(DbContextFactory.Services(_tenant))
            .UseTenantry()
            .Options;

        // On EF Core 10, EF Core's own check of Tenantry's singleton query interceptor may throw first; both name
        // UseInternalServiceProvider.
        FluentActions.Invoking(() => new ItemsOnlyContext(options).Model)
            .Should().Throw<InvalidOperationException>().WithMessage("*UseInternalServiceProvider*");
    }

    [Fact]
    public void EntitiesWithTwoTenantKeyTypes_FailToBuildTheModel()
    {
        var db = new TwoKeyTypesContext(DbContextFactory.Options<TwoKeyTypesContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("*implements ITenantEntity<*, but '*' implements ITenantEntity<*one tenant key type*")
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration);
    }

    [Fact]
    public void EntitiesOfAKeyTypeTenantryIsNotRegisteredFor_FailToBuildTheModel()
    {
        var db = new GuidItemsContext(DbContextFactory.Options<GuidItemsContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*ITenantEntity<Guid>, but Tenantry is not registered for 'Guid' tenant keys*AddTenantry<Guid>*");
    }

    [Fact]
    public async Task InheritanceHierarchy_DerivedTypesAreFilteredThroughTheRoot()
    {
        await using var db = await CreateAsync<HierarchyContext>();
        db.Animals.Add(new Dog { Name = "acme dog" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _tenant.As("globex");
        db.Animals.Add(new Dog { Name = "globex dog" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        (await db.Dogs.Select(dog => dog.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Equal("globex dog");
        (await db.Animals.Select(animal => animal.Name).ToListAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Equal("globex dog");
    }

    [Fact]
    public void TenantOwnedTypeDerivedFromAnUnownedType_FailsToBuildTheModel()
    {
        var db = new UnownedBaseContext(DbContextFactory.Options<UnownedBaseContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Entity 'Car' is tenant-owned but its base entity type 'Vehicle' is not*");
    }

    [Fact]
    public async Task OwnedTenantOwnedType_IsStampedAndIsolatedThroughItsOwner()
    {
        await using var db = await CreateAsync<OwnedContext>();
        await SeedCustomerAsync(db);

        var customer = await db.Customers.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        customer.Addresses.Should().ContainSingle().Which.TenantId.Should().Be("acme");

        _tenant.As("globex");
        db.ChangeTracker.Clear();
        (await db.Customers.ToListAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task OwnedTenantOwnedType_ForgedWriteToAnotherTenantsRow_MatchesNoRow()
    {
        await using var db = await CreateAsync<OwnedContext>();
        var (customerId, addressId) = await SeedCustomerAsync(db);
        _tenant.As("globex");

        // The caller knows acme's keys and claims the rows are its own.
        Address forgedAddress = new() { Id = addressId, TenantId = "globex", Street = "overwritten" };
        Customer forged = new() { Id = customerId, TenantId = "globex", Name = "acme customer", Addresses = [forgedAddress] };
        db.Customers.Attach(forged);
        db.Entry(forgedAddress).Property(address => address.Street).IsModified = true;

        await db.Awaiting(context => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        _tenant.As("acme");
        db.ChangeTracker.Clear();
        (await db.Customers.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Addresses.Should().ContainSingle().Which.Street.Should().Be("acme street");
    }

    [Fact]
    public void OwnedTenantOwnedTypeOfAnUnownedOwner_FailsToBuildTheModel()
    {
        var db = new UnownedOwnerContext(DbContextFactory.Options<UnownedOwnerContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'Price' is tenant-owned but its owner 'Catalog' is not*");
    }

    [Fact]
    public void OwnedTypeWithoutATenantId_KeyedWithoutItsOwner_FailsToBuildTheModel()
    {
        var db = new OwnKeyNoteContext(DbContextFactory.Options<OwnKeyNoteContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'Note' has no TenantId and a key that does not include its owner's key*")
            .Which.Kind.Should().Be(TenantIsolationViolationKind.ModelConfiguration);
    }

    [Fact]
    public void OwnedThroughAnAlternateKeyWithoutTheTenantId_FailsToBuildTheModel()
    {
        // The owner Tenantry checks, by its primary key, need not be the row the owned rows' foreign key names.
        var db = new CodeOwnedNoteContext(DbContextFactory.Options<CodeOwnedNoteContext>(_tenant, _connection));

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'Note' is owned through a key of 'Account' that neither includes nor is part of its primary key, nor includes its TenantId*");
    }

    [Fact]
    public async Task AnEntityThatIsNotTenantOwned_SharingATenantOwnedEntitysTable_FailsOnFirstQuery()
    {
        // Table splitting: it would read and change every tenant's rows of that table, with no filter or TenantId.
        await using var db = new SharedTableContext(DbContextFactory.Options<SharedTableContext>(_tenant, _connection));

        await db.Awaiting(context => context.Set<AccountNote>().ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("Entity 'AccountNote' shares table 'Accounts' with tenant-owned 'Account' but is not tenant-owned*");
    }

    [Fact]
    public async Task ATenantOwnedEntity_SharingATenantOwnedEntitysTable_IsAllowed()
    {
        await using var db = await CreateAsync<TenantSharedTableContext>();

        await db.Awaiting(context => context.Set<TenantAccountNote>().ToListAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TableNamesATenantOwnedTypeDoesNotMapTo_CanBeUsedByOtherEntities()
    {
        // An abstract table-per-concrete-type root, and a type mapped to a view, have no table of their own, whatever
        // their DbSet's name suggests before the model is finished.
        await using var db = await CreateAsync<UnmappedTableNamesContext>();

        await db.Awaiting(context => context.Set<LegacyPayment>().ToListAsync()).Should().NotThrowAsync();
        await db.Awaiting(context => context.Set<ArchivedReport>().ToListAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public void OwnedTypeWithoutATenantId_KeyedWithoutItsOwner_IsAllowedUnderAnUnownedOwner()
    {
        var db = new UnownedOwnKeyNoteContext(DbContextFactory.Options<UnownedOwnKeyNoteContext>(_tenant, _connection));

        db.Invoking(context => context.Model).Should().NotThrow();
    }

    // ── Models that lost their isolation after UseTenantry() built them ─────────────────────────────────────
    // Simulated with Tenantry's interceptors on a context whose model Tenantry's customizer did not build. Each of
    // these context types is used only without UseTenantry(), so its cached model is never one Tenantry built.

    [Fact]
    public async Task ModelWithoutTheTenantFilter_FailsOnFirstQuery()
    {
        await using var db = await CreateWithoutCustomizerAsync<NoCustomizerContext>();

        await db.Awaiting(context => context.Items.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*")
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration && e.TypeName == "Item");
    }

    [Fact]
    public async Task ModelWithoutTheTenantFilter_FailsOnFirstSave_WithoutWriting()
    {
        await using var db = await CreateWithoutCustomizerAsync<NoCustomizerContext>();
        db.Items.Add(new Item { Name = "acme active" });

        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*");
        CountRows("Items").Should().Be(0);
    }

    [Fact]
    public async Task ModelWhoseTenantIdIsNotAConcurrencyToken_FailsOnFirstSave()
    {
        await using var db = await CreateWithoutCustomizerAsync<TokenResetAfterTenantryContext>();
        db.Items.Add(new Item { Name = "acme active" });

        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' is not a concurrency token*");
    }

    [Fact]
    public async Task ModelWithAnOwnedTypeOfAnUnownedOwner_FailsOnFirstQuery()
    {
        await using var db = await CreateWithoutCustomizerAsync<NoCustomizerOwnerContext>();

        await db.Awaiting(context => context.Catalogs.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'Price' is tenant-owned but its owner 'Catalog' is not*");
    }

    private static async Task<(int CustomerId, int AddressId)> SeedCustomerAsync(OwnedContext db)
    {
        Customer customer = new() { Name = "acme customer", Addresses = [new Address { Street = "acme street" }] };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (customer.Id, customer.Addresses[0].Id);
    }

    private DbContextOptions<TContext> Options<TContext>(Action<IServiceCollection> configure)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(DbContextFactory.Services(_tenant, configure: configure))
            .UseTenantry()
            .Options;

    private async Task<TContext> CreateAsync<TContext>()
        where TContext : DbContext
    {
        var db = (TContext)Activator.CreateInstance(typeof(TContext), DbContextFactory.Options<TContext>(_tenant, _connection))!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private async Task<TContext> CreateWithoutCustomizerAsync<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(DbContextFactory.Services(_tenant))
            .AddInterceptors(TenantSaveChangesInterceptor.Instance, TenantQueryInterceptor.Instance)
            .Options;
        var db = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private long CountRows(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    // ── Entities ─────────────────────────────────────────────────────────────

    public sealed class Item : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class LateItem : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class ItemName : ITenantEntity<string>
    {
        public string TenantId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    public sealed class IntItem : ITenantEntity<int>
    {
        public int Id { get; set; }

        public int TenantId { get; set; }
    }

    public sealed class ExplicitItem : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string OrganizationId { get; set; } = string.Empty;

        string ITenantEntity<string>.TenantId => OrganizationId;
    }

    public sealed class GuidItem : ITenantEntity<Guid>
    {
        public int Id { get; set; }

        public Guid TenantId { get; set; }
    }

    public class Animal : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class Dog : Animal;

    public class Vehicle
    {
        public int Id { get; set; }
    }

    public sealed class Car : Vehicle, ITenantEntity<string>
    {
        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Customer : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        public List<Address> Addresses { get; set; } = [];
    }

    public sealed class Address : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Street { get; set; } = string.Empty;
    }

    public sealed class Account : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(16)]
        public string Code { get; set; } = string.Empty;

        public List<Note> Notes { get; set; } = [];
    }

    public abstract class Payment : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class CardPayment : Payment;

    public sealed class LegacyPayment
    {
        public int Id { get; set; }
    }

    public sealed class Report : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class ArchivedReport
    {
        public int Id { get; set; }
    }

    // Shares the Accounts table, not tenant-owned.
    public sealed class AccountNote
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    // Shares the Accounts table, tenant-owned.
    public sealed class TenantAccountNote : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    // Not tenant-owned.
    public sealed class Notebook
    {
        public int Id { get; set; }

        public List<Note> Notes { get; set; } = [];
    }

    public sealed class Note
    {
        // EF Core sets it when it reads or saves the entity.
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public int Id { get; set; }

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    public sealed class Catalog
    {
        public int Id { get; set; }

        public List<Price> Prices { get; set; } = [];
    }

    public sealed class Price : ITenantEntity<string>
    {
        // EF Core sets it when it reads or saves the entity.
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public decimal Amount { get; set; }
    }

    // ── Contexts (one type per model, because EF Core caches the model per context type) ──

    // A context's DbSet properties name its entity types for EF Core, which reads them by reflection.
    // ReSharper disable UnusedMember.Local

    public sealed class ItemsOnlyContext(DbContextOptions<ItemsOnlyContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class ContributedContext(DbContextOptions<ContributedContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class EntityAddedLastContext(DbContextOptions<EntityAddedLastContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Item>().HasQueryFilter(item => item.Name != "hidden");
            modelBuilder.Entity<LateItem>();
        }
    }

    private sealed class KeylessContext(DbContextOptions<KeylessContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        public DbSet<ItemName> ItemNames => Set<ItemName>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ItemName>().HasNoKey().ToSqlQuery("SELECT TenantId, Name FROM Items");
    }

    private sealed class IntKeyContext(DbContextOptions<IntKeyContext> options) : DbContext(options)
    {
        public DbSet<IntItem> Items => Set<IntItem>();
    }

    private sealed class SharedTypeContext(DbContextOptions<SharedTypeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.SharedTypeEntity<LateItem>("CurrentItems");
            modelBuilder.SharedTypeEntity<LateItem>("ArchivedItems");
        }
    }

    private sealed class TokenResetContext(DbContextOptions<TokenResetContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Item>().Property(item => item.TenantId).IsConcurrencyToken(false);
    }

    private sealed class ExplicitTenantIdContext(DbContextOptions<ExplicitTenantIdContext> options) : DbContext(options)
    {
        public DbSet<ExplicitItem> Items => Set<ExplicitItem>();
    }

    public sealed class JsonOrder : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<JsonLine> Lines { get; } = [];
    }

    public sealed class JsonLine : ITenantEntity<string>
    {
        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class JsonOwnedContext(DbContextOptions<JsonOwnedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<JsonOrder>().OwnsMany(order => order.Lines, line => line.ToJson());
    }

#if EFCORE10_OR_GREATER
    private sealed class ReservedFilterNameContext(DbContextOptions<ReservedFilterNameContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Item>().HasQueryFilter(TenantryQueryFilters.Tenant, item => item.Name != "");
    }
#endif

    private sealed class TwoKeyTypesContext(DbContextOptions<TwoKeyTypesContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        public DbSet<GuidItem> GuidItems => Set<GuidItem>();
    }

    private sealed class GuidItemsContext(DbContextOptions<GuidItemsContext> options) : DbContext(options)
    {
        public DbSet<GuidItem> Items => Set<GuidItem>();
    }

    private sealed class HierarchyContext(DbContextOptions<HierarchyContext> options) : DbContext(options)
    {
        public DbSet<Animal> Animals => Set<Animal>();

        public DbSet<Dog> Dogs => Set<Dog>();
    }

    private sealed class UnownedBaseContext(DbContextOptions<UnownedBaseContext> options) : DbContext(options)
    {
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();

        public DbSet<Car> Cars => Set<Car>();
    }

    private sealed class OwnedContext(DbContextOptions<OwnedContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Customer>().OwnsMany(customer => customer.Addresses, address => address.HasKey(a => a.Id));
    }

    private sealed class UnownedOwnerContext(DbContextOptions<UnownedOwnerContext> options) : DbContext(options)
    {
        public DbSet<Catalog> Catalogs => Set<Catalog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Catalog>().OwnsMany(catalog => catalog.Prices, price => price.HasKey(p => p.Id));
    }

    private sealed class OwnKeyNoteContext(DbContextOptions<OwnKeyNoteContext> options) : DbContext(options)
    {
        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Account>().OwnsMany(account => account.Notes, note => note.HasKey(n => n.Id));
    }

    private sealed class CodeOwnedNoteContext(DbContextOptions<CodeOwnedNoteContext> options) : DbContext(options)
    {
        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>().HasAlternateKey(account => account.Code);
            modelBuilder.Entity<Account>().OwnsMany(
                account => account.Notes,
                note => note.WithOwner().HasForeignKey("AccountCode").HasPrincipalKey(account => account.Code));
        }
    }

    private sealed class UnmappedTableNamesContext(DbContextOptions<UnmappedTableNamesContext> options) : DbContext(options)
    {
        public DbSet<Payment> Payments => Set<Payment>();

        public DbSet<Report> Reports => Set<Report>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Payment>().UseTpcMappingStrategy();
            modelBuilder.Entity<CardPayment>().ToTable("CardPayments");
            modelBuilder.Entity<LegacyPayment>().ToTable("Payments");
            modelBuilder.Entity<Report>().ToView("TenantReports");
            modelBuilder.Entity<ArchivedReport>().ToTable("Reports");
        }
    }

    private sealed class SharedTableContext(DbContextOptions<SharedTableContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>(account =>
            {
                account.Ignore(a => a.Notes);
                account.ToTable("Accounts");
            });
            modelBuilder.Entity<AccountNote>(note =>
            {
                note.ToTable("Accounts");
                note.HasOne<Account>().WithOne().HasForeignKey<AccountNote>(n => n.Id);
            });
        }
    }

    private sealed class TenantSharedTableContext(DbContextOptions<TenantSharedTableContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>(account =>
            {
                account.Ignore(a => a.Notes);
                account.ToTable("Accounts");
            });
            modelBuilder.Entity<TenantAccountNote>(note =>
            {
                note.ToTable("Accounts");
                note.HasOne<Account>().WithOne().HasForeignKey<TenantAccountNote>(n => n.Id);
            });
        }
    }

    private sealed class UnownedOwnKeyNoteContext(DbContextOptions<UnownedOwnKeyNoteContext> options) : DbContext(options)
    {
        public DbSet<Notebook> Notebooks => Set<Notebook>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Notebook>().OwnsMany(notebook => notebook.Notes, note => note.HasKey(n => n.Id));
    }

    // Used only without UseTenantry().
    private sealed class NoCustomizerContext(DbContextOptions<NoCustomizerContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    // Used only without UseTenantry().
    private sealed class NoCustomizerOwnerContext(DbContextOptions<NoCustomizerOwnerContext> options) : DbContext(options)
    {
        public DbSet<Catalog> Catalogs => Set<Catalog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Catalog>().OwnsMany(catalog => catalog.Prices, price => price.HasKey(p => p.Id));
    }

    // Used only without UseTenantry(): what its customizer does, followed by configuration that undoes part of it.
    private sealed class TokenResetAfterTenantryContext(DbContextOptions<TokenResetAfterTenantryContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>();
            TenantIsolation.ForModel(modelBuilder.Model)!.ConfigureModel(modelBuilder, this, services: null);
            modelBuilder.Entity<Item>().Property(item => item.TenantId).IsConcurrencyToken(false);
        }
    }

    // ReSharper restore UnusedMember.Local

    private sealed class LateItemContributor : ITenantModelContributor
    {
        public void Configure(ModelBuilder modelBuilder, DbContext context)
        {
            // OnModelCreating has run: its entity types are in the model.
            modelBuilder.Model.FindEntityType(typeof(Item)).Should().NotBeNull();
            modelBuilder.Entity<LateItem>().ToTable("ContributedItems");
        }
    }

    private sealed class OtherModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies);
}
