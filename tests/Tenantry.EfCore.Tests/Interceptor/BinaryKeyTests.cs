using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Keys compared as EF Core compares them: a byte array key by its contents, not by the array it is in. Two entries
/// with equal byte array keys are one row to EF Core, and an owned entity's foreign key is another array than its
/// owner's key.
/// </summary>
public sealed class BinaryKeyTests : IDisposable
{
    private static readonly byte[] Key = [1, 2, 3];

    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ReplacingAnotherTenantsRow_OverMoreThanOneTable_ThroughAStubDeletedAndAddedAgain_IsRejected()
    {
        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            db.Add(new Dog { Id = [.. Key], Detail = "acme detail" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            db.Remove(new Dog { Id = [.. Key], TenantId = "globex", Detail = "acme detail" });
            db.Add(new Dog { Id = [.. Key], Detail = "overwritten by globex" });

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        await using var acme = await CreateAsync(_tenant.As("acme"));
        (await acme.Set<Dog>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Detail.Should().Be("acme detail");
    }

    [Fact]
    public async Task AnOwnedEntityOfAnOwnerWithAByteArrayKey_IsFoundThroughIt()
    {
        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            db.Add(new Order { Id = [.. Key], Lines = { new Line { Id = 1, Text = "acme line" } } });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            var order = await db.Set<Order>().SingleAsync(TestContext.Current.CancellationToken);
            order.Lines[0].Text = "changed";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            Order stub = new() { Id = [.. Key], TenantId = "globex" };
            db.Attach(stub);
            stub.Lines.Add(new Line { Id = 2, Text = "from globex" });

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        await using var acme = await CreateAsync(_tenant.As("acme"));
        (await acme.Set<Order>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Lines
            .Select(line => line.Text).Should().Equal("changed");
    }

    private async Task<BinaryKeyContext> CreateAsync(TestTenantContext tenant)
    {
        BinaryKeyContext db = new(DbContextFactory.Options<BinaryKeyContext>(tenant, _connection));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public class Animal : ITenantEntity<string>
    {
        [MaxLength(16)]
        public byte[] Id { get; set; } = [];

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Dog : Animal
    {
        [MaxLength(64)]
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class Order : ITenantEntity<string>
    {
        [MaxLength(16)]
        public byte[] Id { get; set; } = [];

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Line> Lines { get; } = [];
    }

    public sealed class Line
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class BinaryKeyContext(DbContextOptions<BinaryKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>().UseTptMappingStrategy();
            modelBuilder.Entity<Dog>().ToTable("Dogs");
            modelBuilder.Entity<Order>().OwnsMany(order => order.Lines, line => line.Property(l => l.Id).ValueGeneratedNever());
        }
    }
}
