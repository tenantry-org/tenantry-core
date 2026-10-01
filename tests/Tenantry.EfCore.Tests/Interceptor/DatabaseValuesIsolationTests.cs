using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// EF Core reads an entity's database values (<c>Reload</c>, <c>GetDatabaseValues</c>) by its key with
/// <c>IgnoreQueryFilters()</c>. Tenantry keeps the tenant filter on that query, so the key of another tenant's row
/// reads as a deleted row, however the entity came to have it.
/// </summary>
public sealed class DatabaseValuesIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetDatabaseValues_OfAnotherTenantsRow_IsNull()
    {
        var acmeOrderId = await SeedOrderAsync("acme", "acme secret");

        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);
        var forged = db.Attach(new Order { Id = acmeOrderId, TenantId = "globex" });

        forged.GetDatabaseValues().Should().BeNull();
        (await forged.GetDatabaseValuesAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Reload_OfAnotherTenantsRow_DetachesTheEntityWithoutItsValues()
    {
        var acmeOrderId = await SeedOrderAsync("acme", "acme secret");

        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);
        Order forged = new() { Id = acmeOrderId, TenantId = "globex", Description = "forged" };
        db.Attach(forged);

        db.Entry(forged).Reload();

        db.Entry(forged).State.Should().Be(EntityState.Detached);
        forged.Description.Should().Be("forged");

        db.Attach(forged);
        await db.Entry(forged).ReloadAsync();

        db.Entry(forged).State.Should().Be(EntityState.Detached);
        forged.Description.Should().Be("forged");
    }

    [Fact]
    public async Task ConcurrencyHandler_AfterAForgedWrite_CannotReadTheOtherTenantsRow()
    {
        var acmeOrderId = await SeedOrderAsync("acme", "acme secret");

        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);
        db.Orders.Update(new Order { Id = acmeOrderId, TenantId = "globex", Description = "overwritten" });

        var conflict = (await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>()).Which;

        (await conflict.Entries.Should().ContainSingle().Which.GetDatabaseValuesAsync()).Should().BeNull();
    }

    [Fact]
    public async Task OwnRow_ReloadsItsValues()
    {
        var orderId = await SeedOrderAsync("acme", "stored");

        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        order.Description = "changed";

        (await db.Entry(order).GetDatabaseValuesAsync())!["Description"].Should().Be("stored");

        await db.Entry(order).ReloadAsync();

        order.Description.Should().Be("stored");
        db.Entry(order).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task WithoutTenant_GetDatabaseValues_OfATenantsRow_IsNull()
    {
        // Like every query, it reads nothing without a tenant.
        var acmeOrderId = await SeedOrderAsync("acme", "acme secret");

        await using var db = await DbContextFactory.CreateContextAsync(_tenant.AsNone(), _connection);

        db.Attach(new Order { Id = acmeOrderId, TenantId = "acme" }).GetDatabaseValues().Should().BeNull();
    }

    [Fact]
    public async Task NonTenantEntity_IsReadAsBefore()
    {
        await using (var seed = await DbContextFactory.CreateContextAsync(_tenant.AsNone(), _connection))
        {
            seed.NonTenants.Add(new NonTenant { Id = 1, Name = "catalogue" });
            await seed.SaveChangesAsync();
        }

        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);

        db.Attach(new NonTenant { Id = 1 }).GetDatabaseValues()!["Name"].Should().Be("catalogue");
    }

    [Fact]
    public async Task OwnedEntities_OfAnotherTenantsOwner_AreNotRead()
    {
        // EF Core reads an owned entity's values through its owner, so the owner's tenant filter covers them.
        await using (var seed = await CreateAsync(_tenant.As("acme")))
        {
            seed.Customers.Add(new Customer { Id = 1, Address = new Address { City = "acme city" }, Phones = { new Phone { Id = 1, Number = "acme phone" } } });
            await seed.SaveChangesAsync();
        }

        await using (var own = await CreateAsync(_tenant.As("acme")))
        {
            var customer = await own.Customers.SingleAsync();
            own.Entry(customer.Address!).GetDatabaseValues()!["City"].Should().Be("acme city");
            own.Entry(customer.Phones[0]).GetDatabaseValues()!["Number"].Should().Be("acme phone");
        }

        await using var db = await CreateAsync(_tenant.As("globex"));
        Customer forged = new() { Id = 1, TenantId = "globex", Address = new Address { City = "?" }, Phones = { new Phone { Id = 1, Number = "?" } } };
        db.Attach(forged);

        db.Entry(forged.Address!).GetDatabaseValues().Should().BeNull();
        db.Entry(forged.Phones[0]).GetDatabaseValues().Should().BeNull();
    }

    [Fact]
    public async Task DerivedEntity_OfAnotherTenantsRow_IsNotRead()
    {
        await using (var seed = await CreateAsync(_tenant.As("acme")))
        {
            seed.Add(new PremiumCustomer { Id = 2, Level = "gold" });
            await seed.SaveChangesAsync();
        }

        await using var db = await CreateAsync(_tenant.As("globex"));

        db.Attach(new PremiumCustomer { Id = 2, TenantId = "globex" }).GetDatabaseValues().Should().BeNull();
    }

    private async Task<int> SeedOrderAsync(string tenantId, string description)
    {
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As(tenantId), _connection);
        Order order = new() { Description = description };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<CustomersContext> CreateAsync(TestTenantContext tenant)
    {
        CustomersContext db = new(DbContextFactory.Options<CustomersContext>(tenant, _connection));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public class Customer : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public Address? Address { get; set; }

        public List<Phone> Phones { get; set; } = [];
    }

    public sealed class PremiumCustomer : Customer
    {
        [MaxLength(64)]
        public string Level { get; set; } = string.Empty;
    }

    public sealed class Address
    {
        [MaxLength(64)]
        public string City { get; set; } = string.Empty;
    }

    public sealed class Phone
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Number { get; set; } = string.Empty;
    }

    public sealed class CustomersContext(DbContextOptions<CustomersContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(customer =>
            {
                customer.OwnsOne(c => c.Address);
                customer.OwnsMany(c => c.Phones, phone => phone.Property(p => p.Id).ValueGeneratedNever());
            });
            modelBuilder.Entity<PremiumCustomer>();
        }
    }
}
