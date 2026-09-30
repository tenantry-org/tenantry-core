using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.EfCore.Extensions;

namespace Tenantry.EfCore.Tests.ModelBuilding;

/// <summary>
/// A context with Tenantry's interceptors checks its model on the first query and save: every tenant-scoped entity
/// type must keep the tenant filter and concurrency token, whichever order the model was configured in.
/// </summary>
public sealed class TenantModelCheckTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.For("acme");

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task OwnFilterBeforeTenantFilter_BothApply()
    {
        await using var db = await CreateAsync<FilterBeforeContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
    }

    [Fact]
    public async Task UnnamedOwnFilterAfterTenantFilter_FailsInsteadOfReadingEveryTenant()
    {
        await using var db = new FilterAfterContext(DbContextFactory.InterceptorOptions<FilterAfterContext>(_tenant, _connection), _tenant);

#if NET10_0_OR_GREATER
        // EF Core 10 does not combine an unnamed filter with the named tenant filter.
        db.Invoking(context => context.Model).Should().Throw<InvalidOperationException>();
#else
        // The unnamed filter replaced the tenant filter.
        await db.Database.EnsureCreatedAsync();
        await db.Awaiting(context => context.Items.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*");
#endif
    }

#if NET10_0_OR_GREATER
    [Fact]
    public async Task NamedOwnFilterAfterTenantFilter_BothApply()
    {
        await using var db = await CreateAsync<NamedFilterAfterContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
    }
#endif

    [Fact]
    public async Task MultiTenantDbContext_OwnFilterBeforeBase_BothApply()
    {
        await using var db = await CreateAsync<BaseLastContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
    }

    [Fact]
    public async Task MultiTenantDbContext_UnnamedOwnFilterAfterBase_FailsInsteadOfReadingEveryTenant()
    {
        await using var db = new BaseFirstContext(DbContextFactory.InterceptorOptions<BaseFirstContext>(_tenant, _connection), _tenant);

#if NET10_0_OR_GREATER
        db.Invoking(context => context.Model).Should().Throw<InvalidOperationException>();
#else
        await db.Database.EnsureCreatedAsync();
        await db.Awaiting(context => context.Items.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*");
#endif
    }

    [Fact]
    public async Task OwnFilterBeforeAndAnotherAfterTenantFilter_FailsInsteadOfReadingEveryTenant()
    {
        // ApplyTenantFilters merges the tenant filter into the unnamed own filter; an unnamed filter after it
        // replaces the merged one, on EF Core 10 too.
        await using var db = await CreateAsync<FilterBeforeAndAfterContext>();

        await db.Awaiting(context => context.Items.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*");
    }

    [Fact]
    public async Task EntityTypeAddedAfterTenantFilter_FailsOnFirstQuery()
    {
        await using var db = await CreateAsync<EntityAddedAfterContext>();

        await db.Awaiting(context => context.Set<LateItem>().ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'LateItem' has no tenant query filter*");
    }

    [Fact]
    public async Task NoTenantFilters_FailOnFirstQuery()
    {
        await using var db = await CreateAsync<NoFiltersContext>();

        await db.Awaiting(context => context.Items.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*");
    }

    [Fact]
    public async Task NoTenantFilters_FailOnFirstSave_WithoutWriting()
    {
        await using var db = await CreateAsync<NoFiltersContext>();
        db.Items.Add(new Item { Name = "acme active" });

        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' has no tenant query filter*");
        CountRows("Items").Should().Be(0);
    }

    [Fact]
    public async Task EntityWithAnotherKeyType_ApplyTenantFiltersThrows()
    {
        await using var db = new OtherKeyContext(DbContextFactory.InterceptorOptions<OtherKeyContext>(_tenant, _connection), _tenant);

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Entity 'GuidItem' implements ITenantScoped<Guid>, but the tenant key type here is String*");
    }

    [Fact]
    public async Task EntityWithAnotherKeyType_WithoutTenantFilters_FailsOnFirstQuery()
    {
        await using var db = await CreateAsync<OtherKeyNoFiltersContext>();

        await db.Awaiting(context => context.Items.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*implements ITenantScoped<Guid>*");
    }

    [Fact]
    public async Task TenantIdNotAConcurrencyToken_FailsOnFirstSave()
    {
        await using var db = await CreateAsync<TokenResetContext>();
        db.Items.Add(new Item { Name = "acme active" });

        await db.Awaiting(context => context.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*'Item' is not a concurrency token*");
    }

    [Fact]
    public async Task InheritanceHierarchy_DerivedTypesAreFilteredThroughTheRoot()
    {
        await using var db = await CreateAsync<HierarchyContext>();
        db.Animals.Add(new Dog { Name = "acme dog" });
        await db.SaveChangesAsync();
        _tenant.As("globex");
        db.Animals.Add(new Dog { Name = "globex dog" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.Dogs.Select(dog => dog.Name).ToListAsync()).Should().Equal("globex dog");
        (await db.Animals.Select(animal => animal.Name).ToListAsync()).Should().Equal("globex dog");
    }

    [Fact]
    public async Task TenantScopedTypeDerivedFromAnUnscopedType_ApplyTenantFiltersThrows()
    {
        await using var db = new UnscopedBaseContext(DbContextFactory.InterceptorOptions<UnscopedBaseContext>(_tenant, _connection), _tenant);

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Entity 'Car' is tenant-scoped but its base entity type 'Vehicle' is not*");
    }

    [Fact]
    public async Task OwnedTenantScopedType_IsStampedAndIsolatedThroughItsOwner()
    {
        await using var db = await CreateAsync<OwnedContext>();
        await SeedCustomerAsync(db);

        var customer = await db.Customers.SingleAsync();
        customer.Addresses.Should().ContainSingle().Which.TenantId.Should().Be("acme");

        _tenant.As("globex");
        db.ChangeTracker.Clear();
        (await db.Customers.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OwnedTenantScopedType_ForgedWriteToAnotherTenantsRow_MatchesNoRow()
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
        (await db.Customers.SingleAsync()).Addresses.Should().ContainSingle().Which.Street.Should().Be("acme street");
    }

    [Fact]
    public async Task OwnedTenantScopedTypeOfAnUnscopedOwner_ApplyTenantFiltersThrows()
    {
        await using var db = new UnscopedOwnerContext(DbContextFactory.InterceptorOptions<UnscopedOwnerContext>(_tenant, _connection), _tenant);

        db.Invoking(context => context.Model)
            .Should().Throw<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'Price' is tenant-scoped but its owner 'Catalog' is not*");
    }

    [Fact]
    public async Task OwnedTenantScopedTypeOfAnUnscopedOwner_WithoutTenantFilters_FailsOnFirstQuery()
    {
        await using var db = await CreateAsync<UnscopedOwnerNoFiltersContext>();

        await db.Awaiting(context => context.Catalogs.ToListAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("Owned entity 'Price' is tenant-scoped but its owner 'Catalog' is not*");
    }

    private static async Task<(int CustomerId, int AddressId)> SeedCustomerAsync(OwnedContext db)
    {
        Customer customer = new() { Name = "acme customer", Addresses = [new Address { Street = "acme street" }] };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (customer.Id, customer.Addresses[0].Id);
    }

    private async Task<TContext> CreateAsync<TContext>()
        where TContext : DbContext
    {
        var db = (TContext)Activator.CreateInstance(typeof(TContext), DbContextFactory.InterceptorOptions<TContext>(_tenant, _connection), _tenant)!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private async Task SeedAsync(IItems db)
    {
        db.Items.Add(new Item { Name = "acme active" });
        db.Items.Add(new Item { Name = "acme deleted", IsDeleted = true });
        await ((DbContext)db).SaveChangesAsync();
        _tenant.As("globex");
        db.Items.Add(new Item { Name = "globex active" });
        await ((DbContext)db).SaveChangesAsync();
        _tenant.As("acme");
        ((DbContext)db).ChangeTracker.Clear();
    }

    private long CountRows(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    // ── Entities ─────────────────────────────────────────────────────────────

    public sealed class Item : ITenantScoped<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        public bool IsDeleted { get; set; }
    }

    public sealed class LateItem : ITenantScoped<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class GuidItem : ITenantScoped<Guid>
    {
        public int Id { get; set; }

        public Guid TenantId { get; set; }
    }

    public class Animal : ITenantScoped<string>
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

    public sealed class Car : Vehicle, ITenantScoped<string>
    {
        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Customer : ITenantScoped<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        public List<Address> Addresses { get; set; } = [];
    }

    public sealed class Address : ITenantScoped<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Street { get; set; } = string.Empty;
    }

    public sealed class Catalog
    {
        public int Id { get; set; }

        public List<Price> Prices { get; set; } = [];
    }

    public sealed class Price : ITenantScoped<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public decimal Amount { get; set; }
    }

    // ── Contexts (one type per model, because EF Core caches the model per context type) ──

    private interface IItems
    {
        DbSet<Item> Items { get; }
    }

    private abstract class ItemsContext(DbContextOptions options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>, IItems
    {
        public DbSet<Item> Items => Set<Item>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;
    }

    private sealed class FilterBeforeContext(DbContextOptions<FilterBeforeContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
            modelBuilder.ApplyTenantFilters<string, FilterBeforeContext>(this);
        }
    }

    private sealed class FilterAfterContext(DbContextOptions<FilterAfterContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyTenantFilters<string, FilterAfterContext>(this);
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
        }
    }

#if NET10_0_OR_GREATER
    private sealed class NamedFilterAfterContext(DbContextOptions<NamedFilterAfterContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyTenantFilters<string, NamedFilterAfterContext>(this);
            modelBuilder.Entity<Item>().HasQueryFilter("SoftDelete", item => !item.IsDeleted);
        }
    }
#endif

    private sealed class BaseLastContext(DbContextOptions<BaseLastContext> options, ITenantContext<string> tenantContext)
        : MultiTenantDbContext<string>(options, tenantContext), IItems
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class BaseFirstContext(DbContextOptions<BaseFirstContext> options, ITenantContext<string> tenantContext)
        : MultiTenantDbContext<string>(options, tenantContext), IItems
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
        }
    }

    private sealed class FilterBeforeAndAfterContext(DbContextOptions<FilterBeforeAndAfterContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
            modelBuilder.ApplyTenantFilters<string, FilterBeforeAndAfterContext>(this);
            modelBuilder.Entity<Item>().HasQueryFilter(item => item.Name != "");
        }
    }

    private sealed class EntityAddedAfterContext(DbContextOptions<EntityAddedAfterContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyTenantFilters<string, EntityAddedAfterContext>(this);
            modelBuilder.Entity<LateItem>();
        }
    }

    private sealed class NoFiltersContext(DbContextOptions<NoFiltersContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext);

    private sealed class OtherKeyContext(DbContextOptions<OtherKeyContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        public DbSet<GuidItem> GuidItems => Set<GuidItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyTenantFilters<string, OtherKeyContext>(this);
    }

    private sealed class OtherKeyNoFiltersContext(DbContextOptions<OtherKeyNoFiltersContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<GuidItem> Items => Set<GuidItem>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;
    }

    private sealed class TokenResetContext(DbContextOptions<TokenResetContext> options, ITenantContext<string> tenantContext)
        : ItemsContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyTenantFilters<string, TokenResetContext>(this);
            modelBuilder.Entity<Item>().Property(item => item.TenantId).IsConcurrencyToken(false);
        }
    }

    private sealed class HierarchyContext(DbContextOptions<HierarchyContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<Animal> Animals => Set<Animal>();

        public DbSet<Dog> Dogs => Set<Dog>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyTenantFilters<string, HierarchyContext>(this);
    }

    private sealed class UnscopedBaseContext(DbContextOptions<UnscopedBaseContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();

        public DbSet<Car> Cars => Set<Car>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyTenantFilters<string, UnscopedBaseContext>(this);
    }

    private sealed class OwnedContext(DbContextOptions<OwnedContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<Customer> Customers => Set<Customer>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>().OwnsMany(customer => customer.Addresses, address => address.HasKey(a => a.Id));
            modelBuilder.ApplyTenantFilters<string, OwnedContext>(this);
        }
    }

    private sealed class UnscopedOwnerContext(DbContextOptions<UnscopedOwnerContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<Catalog> Catalogs => Set<Catalog>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Catalog>().OwnsMany(catalog => catalog.Prices, price => price.HasKey(p => p.Id));
            modelBuilder.ApplyTenantFilters<string, UnscopedOwnerContext>(this);
        }
    }

    private sealed class UnscopedOwnerNoFiltersContext(DbContextOptions<UnscopedOwnerNoFiltersContext> options, ITenantContext<string> tenantContext)
        : DbContext(options), ITenantAwareDbContext<string>
    {
        public DbSet<Catalog> Catalogs => Set<Catalog>();

        public string? CurrentTenantId => tenantContext.CurrentTenantId;

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Catalog>().OwnsMany(catalog => catalog.Prices, price => price.HasKey(p => p.Id));
    }
}
