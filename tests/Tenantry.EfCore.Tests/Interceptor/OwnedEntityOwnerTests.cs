using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// An owned entity belongs to its owner, and EF Core reads owned rows through the owner without a tenant filter of
/// their own. Adding one must not let a tenant attach a stub of another tenant's owner and add rows to it.
/// </summary>
public sealed class OwnedEntityOwnerTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AddingToAnotherTenantsOwnedCollection_ThroughAStubWithAForgedTenantId_FailsAndWritesNothing()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Number = "from globex" });

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await PhonesOfAcmeCustomerAsync()).Should().Equal("acme phone");
    }

    [Fact]
    public async Task AddingToAnotherTenantsOwnedCollection_ThroughAStubWithoutItsTenant_IsRejected()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1 };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Number = "from globex" });

            (await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.Kind.Should().Be(TenantIsolationViolationKind.EntityWrite);
        }

        (await PhonesOfAcmeCustomerAsync()).Should().Equal("acme phone");
    }

    [Fact]
    public async Task GivingAnotherTenantsOwnerAnOwnedEntity_ThroughAStub_FailsAndWritesNothing()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            stub.Address = new Address { City = "from globex" };

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        await using var acme = await CreateAsync(_tenant.As("acme"));
        (await acme.Customers.AsNoTracking().SingleAsync()).Address.Should().BeNull();
    }

    [Fact]
    public async Task AddingToTheCurrentTenantsLoadedOwner_Works()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            var customer = await db.Customers.SingleAsync();
            customer.Phones.Add(new Phone { Number = "second acme phone" });
            customer.Address = new Address { City = "Springfield" };
            await db.SaveChangesAsync();
        }

        (await PhonesOfAcmeCustomerAsync()).Should().BeEquivalentTo("acme phone", "second acme phone");
        await using var acme = await CreateAsync(_tenant.As("acme"));
        var saved = await acme.Customers.AsNoTracking().SingleAsync();
        saved.Address!.City.Should().Be("Springfield");
        saved.Address.TenantId.Should().Be("acme");
        saved.Phones.Should().OnlyContain(phone => phone.TenantId == "acme");
    }

    private async Task SeedAcmeCustomerAsync()
    {
        await using var db = await CreateAsync(_tenant.As("acme"));
        db.Customers.Add(new Customer { Id = 1, Phones = { new Phone { Number = "acme phone" } } });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> PhonesOfAcmeCustomerAsync()
    {
        await using var db = await CreateAsync(_tenant.As("acme"));
        var customer = await db.Customers.AsNoTracking().SingleAsync();
        return [.. customer.Phones.Select(phone => phone.Number)];
    }

    private async Task<OwnedContext> CreateAsync(TestTenantContext tenant)
    {
        OwnedContext db = new(DbContextFactory.Options<OwnedContext>(tenant, _connection));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public sealed class Customer : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Phone> Phones { get; } = [];

        public Address? Address { get; set; }
    }

    public sealed class Phone : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Number { get; set; } = string.Empty;
    }

    public sealed class Address : ITenantEntity<string>
    {
        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string City { get; set; } = string.Empty;
    }

    private sealed class OwnedContext(DbContextOptions<OwnedContext> options)
        : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(customer =>
            {
                customer.Property(c => c.Id).ValueGeneratedNever();
                customer.OwnsMany(c => c.Phones, phone => phone.HasKey(p => p.Id));
                customer.OwnsOne(c => c.Address);
            });
        }
    }
}
