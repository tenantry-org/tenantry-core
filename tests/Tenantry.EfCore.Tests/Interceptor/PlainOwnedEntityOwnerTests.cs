using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// An owned type that does not implement <c>ITenantEntity</c> has no <c>TenantId</c> of its own: its rows in their own
/// table carry only their owner's key. Its writes are checked through the owner, like a tenant-owned owned type's.
/// </summary>
public sealed class PlainOwnedEntityOwnerTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AddingThroughAStubWithAForgedTenantId_FailsAndWritesNothing()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Id = 2, Number = "from globex" });

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await AcmePhonesAsync()).Should().Equal("1: acme phone");
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("")]
    public async Task AddingThroughAStubNamingAnotherOrNoTenant_IsRejected(string stubTenantId)
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = stubTenantId };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Id = 2, Number = "from globex" });

            (await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.Kind.Should().Be(TenantIsolationViolationKind.EntityWrite);
        }

        (await AcmePhonesAsync()).Should().Equal("1: acme phone");
    }

    [Fact]
    public async Task ChangingThroughAStub_FailsAndWritesNothing()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "globex", Phones = { new Phone { Id = 1, Number = "acme phone" } } };
            db.Attach(stub);
            stub.Phones[0].Number = "overwritten by globex";

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await AcmePhonesAsync()).Should().Equal("1: acme phone");
    }

    [Fact]
    public async Task DeletingThroughAStub_FailsAndKeepsTheRow()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "globex", Phones = { new Phone { Id = 1 } } };
            db.Attach(stub);
            stub.Phones.Clear();

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await AcmePhonesAsync()).Should().Equal("1: acme phone");
    }

    [Fact]
    public async Task ChangingOwnedEntitiesInTheOwnersRowOrTheirOwnTable_ThroughAStubNamingThatTenant_IsRejected()
    {
        // The owner's row and the owned table's rows would both match a stub that names the owner's real tenant.
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "acme", Home = new Home { City = "acme home" }, Billing = new Billing { City = "acme billing" } };
            db.Attach(stub);
            stub.Home!.City = "globex home";
            stub.Billing!.City = "globex billing";

            (await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.OffendingTenantId.Should().Be("acme");
        }

        await using var acme = await CreateAsync(_tenant.As("acme"));
        var customer = await acme.Customers.AsNoTracking().SingleAsync(c => c.Id == 1);
        customer.Home!.City.Should().Be("acme home");
        customer.Billing!.City.Should().Be("acme billing");
    }

    [Fact]
    public async Task ChangingAnOwnedEntityWithoutItsOwner_IsRejected()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            var phone = db.Entry(new Phone { Id = 1, Number = "overwritten by globex" });
            phone.Property("CustomerId").CurrentValue = 1;
            phone.State = EntityState.Modified;

            (await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("A 'Phone' is being saved without its owner 'Customer'*");
        }

        (await AcmePhonesAsync()).Should().Equal("1: acme phone");
    }

    [Fact]
    public async Task WithoutTenant_OwnedWrites_AreRejectedByDefault()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.AsNone()))
        {
            Customer stub = new() { Id = 1, TenantId = "acme" };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Id = 2, Number = "no tenant" });

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantNotResolvedException>()
                .WithMessage("*(Phone)*");
        }

        (await AcmePhonesAsync()).Should().Equal("1: acme phone");
    }

    [Fact]
    public async Task TheCurrentTenantsLoadedOwner_CanAddChangeAndDeleteOwnedEntities()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            var customer = await db.Customers.SingleAsync(c => c.Id == 1);
            customer.Phones[0].Number = "changed";
            customer.Phones.Add(new Phone { Id = 2, Number = "added" });
            await db.SaveChangesAsync();

            customer.Phones.RemoveAt(0);
            await db.SaveChangesAsync();
        }

        (await AcmePhonesAsync()).Should().Equal("2: added");
    }

    [Fact]
    public async Task NestedOwnedEntities_AreCheckedThroughTheRootOwner()
    {
        await SeedAcmeCustomerAsync();

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            var customer = await db.Customers.SingleAsync(c => c.Id == 1);
            customer.Home!.Location!.Street = "acme new street";
            await db.SaveChangesAsync();
        }

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Customer stub = new() { Id = 1, TenantId = "acme", Home = new Home { City = "acme home", Location = new Location { Street = "acme new street" } } };
            db.Attach(stub);
            stub.Home!.Location!.Street = "globex street";

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        await using var acme = await CreateAsync(_tenant.As("acme"));
        (await acme.Customers.AsNoTracking().SingleAsync(c => c.Id == 1)).Home!.Location!.Street.Should().Be("acme new street");
    }

    [Fact]
    public async Task OwnedEntitiesOfADerivedOwner_AreCheckedThroughIt()
    {
        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            db.Customers.Add(new PremiumCustomer { Id = 2 });
            await db.SaveChangesAsync();
        }

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            var premium = (PremiumCustomer)await db.Customers.SingleAsync(c => c.Id == 2);
            premium.Perk = new Perk { Name = "acme perk" };
            await db.SaveChangesAsync();
        }

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            PremiumCustomer stub = new() { Id = 2, TenantId = "acme" };
            db.Attach(stub);
            stub.Perk = new Perk { Name = "globex perk" };

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        await using var acme = await CreateAsync(_tenant.As("acme"));
        ((PremiumCustomer)await acme.Customers.AsNoTracking().SingleAsync(c => c.Id == 2)).Perk!.Name.Should().Be("acme perk");
    }

    [Fact]
    public async Task OwnedEntitiesOfAGlobalOwner_AreNotChecked()
    {
        await using (var seed = await CreateAsync(_tenant.AsNone()))
        {
            seed.Catalogues.Add(new Catalogue { Id = 1, Entries = { new Entry { Id = 1, Text = "global" } } });
            await seed.SaveChangesAsync();
        }

        await using var db = await CreateAsync(_tenant.As("globex"));
        Catalogue stub = new() { Id = 1 };
        db.Attach(stub);
        stub.Entries.Add(new Entry { Id = 2, Text = "added" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var entry = db.Entry(new Entry { Id = 1, Text = "changed without its owner" });
        entry.Property("CatalogueId").CurrentValue = 1;
        entry.State = EntityState.Modified;

        await db.Awaiting(d => d.SaveChangesAsync()).Should().NotThrowAsync();
    }

    private async Task SeedAcmeCustomerAsync()
    {
        await using var db = await CreateAsync(_tenant.As("acme"));
        db.Customers.Add(new Customer
        {
            Id = 1,
            Phones = { new Phone { Id = 1, Number = "acme phone" } },
            Home = new Home { City = "acme home", Location = new Location { Street = "acme street" } },
            Billing = new Billing { City = "acme billing" },
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> AcmePhonesAsync()
    {
        await using var db = await CreateAsync(_tenant.As("acme"));
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == 1);
        return [.. customer.Phones.OrderBy(phone => phone.Id).Select(phone => $"{phone.Id}: {phone.Number}")];
    }

    private async Task<PlainOwnedContext> CreateAsync(TestTenantContext tenant)
    {
        PlainOwnedContext db = new(DbContextFactory.Options<PlainOwnedContext>(tenant, _connection));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public class Customer : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Phone> Phones { get; } = [];

        public Home? Home { get; set; }

        public Billing? Billing { get; set; }
    }

    // Stored in the owner's row.
    public sealed class Home
    {
        [MaxLength(64)]
        public string City { get; set; } = string.Empty;

        public Location? Location { get; set; }
    }

    // Owned by an owned type.
    public sealed class Location
    {
        [MaxLength(64)]
        public string Street { get; set; } = string.Empty;
    }

    public sealed class PremiumCustomer : Customer
    {
        public Perk? Perk { get; set; }
    }

    // Owned by a derived entity type.
    public sealed class Perk
    {
        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
    }

    // Stored in a table of its own.
    public sealed class Billing
    {
        [MaxLength(64)]
        public string City { get; set; } = string.Empty;
    }

    public sealed class Catalogue
    {
        public int Id { get; set; }

        public List<Entry> Entries { get; } = [];
    }

    public sealed class Entry
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    public sealed class Phone
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Number { get; set; } = string.Empty;
    }

    private sealed class PlainOwnedContext(DbContextOptions<PlainOwnedContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        public DbSet<Catalogue> Catalogues => Set<Catalogue>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(customer =>
            {
                customer.Property(c => c.Id).ValueGeneratedNever();
                customer.OwnsMany(c => c.Phones, phone =>
                {
                    phone.ToTable("CustomerPhones");
                    phone.Property(p => p.Id).ValueGeneratedNever();
                });
                customer.OwnsOne(c => c.Home, home => home.OwnsOne(h => h.Location));
                customer.OwnsOne(c => c.Billing, billing => billing.ToTable("CustomerBilling"));
            });
            modelBuilder.Entity<PremiumCustomer>().OwnsOne(p => p.Perk, perk => perk.ToTable("CustomerPerks"));
            modelBuilder.Entity<Catalogue>(catalogue =>
            {
                catalogue.Property(c => c.Id).ValueGeneratedNever();
                catalogue.OwnsMany(c => c.Entries, entry =>
                {
                    entry.ToTable("CatalogueEntries");
                    entry.Property(p => p.Id).ValueGeneratedNever();
                });
            });
        }
    }
}
