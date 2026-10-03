using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// The shapes whose stored row Tenantry reads before the save: an owner or an entity over more than one table whose
/// <c>TenantId</c> is not written after an insert, and a deleted and added pair over more than one table. The read
/// ignores the application's own query filter, as every other write does, so the current tenant's row that filter
/// hides (an archived one) can still be changed; another tenant's row is still rejected, without naming that tenant.
/// </summary>
public sealed class StoredRowReadTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    public static TheoryData<Shape, bool> Shapes() => new()
    {
        { Shape.TenantIdInAnAlternateKey, false },
        { Shape.TenantIdInAnAlternateKey, true },
        { Shape.TablePerTypeWithTenantIdNotSaved, false },
        { Shape.TablePerTypeWithTenantIdNotSaved, true },
        { Shape.TablePerTypeDeletedAndAdded, false },
        { Shape.TablePerTypeDeletedAndAdded, true },
        { Shape.OwnedCollectionReadThroughItsOwner, false },
        { Shape.OwnedCollectionReadThroughItsOwner, true },
        { Shape.OwnedReferenceReadThroughItsOwner, false },
        { Shape.OwnedReferenceReadThroughItsOwner, true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ChangingTheCurrentTenantsRow_ThatItsOwnFilterHides_Works(Shape shape, bool sync)
    {
        await SeedAsync(shape, "acme", archived: true);

        await using (var db = await CreateAsync(shape, _tenant.As("acme")))
        {
            await ChangeLoadedAsync(db, shape);
            await SaveAsync(db, sync);
        }

        (await StoredTextAsync(shape, "acme")).Should().Be("changed");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ChangingAnotherTenantsRow_ThroughAStub_IsRejected_WithoutNamingThatTenant(Shape shape, bool sync)
    {
        await SeedAsync(shape, "acme", archived: false);

        await using (var db = await CreateAsync(shape, _tenant.As("globex")))
        {
            ChangeThroughStub(db, shape, "globex");

            (await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.Message.Should().Contain("no row with its key is stored for the current tenant 'globex'")
                .And.NotContain("acme");
        }

        (await StoredTextAsync(shape, "acme")).Should().Be("acme text");
    }

    [Fact]
    public async Task TheRead_IgnoresTheApplicationsFilter_AndSendsTheTenantAndTheKeyAsParameters()
    {
        await SeedAsync(Shape.TenantIdInAnAlternateKey, "acme", archived: true);
        CommandLog log = new();

        await using (var db = await CreateAsync(Shape.TenantIdInAnAlternateKey, _tenant.As("acme"), log))
        {
            await ChangeLoadedAsync(db, Shape.TenantIdInAnAlternateKey);
            log.Commands.Clear();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var read = log.Commands.Should().ContainSingle(command => command.Text.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)).Subject;
        read.Text.Should().Contain("\"TenantId\" = @").And.NotContain("Archived");
        read.Values.Should().BeEquivalentTo(new object[] { "acme", 1L });
    }

    public enum Shape
    {
        // An owner keyed on (TenantId, Id) as an alternate key: its TenantId cannot be written back.
        TenantIdInAnAlternateKey,

        // A table-per-type entity whose TenantId is not written after an insert, changed in its derived table.
        TablePerTypeWithTenantIdNotSaved,

        // A table-per-type entity deleted and added again under its key, which EF Core saves as an UPDATE.
        TablePerTypeDeletedAndAdded,

        // A tenant-owned owned type with its own table and TenantId not saved, owning another: read through its owner.
        OwnedCollectionReadThroughItsOwner,

        // The same through a reference navigation.
        OwnedReferenceReadThroughItsOwner,
    }

    private static Task SaveAsync(DbContext db, bool sync)
    {
        if (!sync)
        {
            return db.SaveChangesAsync();
        }

        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task SeedAsync(Shape shape, string tenant, bool archived)
    {
        await using var db = await CreateAsync(shape, _tenant.As(tenant));

        switch (shape)
        {
            case Shape.TenantIdInAnAlternateKey:
                db.Add(new Order { Id = 1, Archived = archived, Lines = { new Line { Id = 1, Text = $"{tenant} text" } } });
                break;
            case Shape.TablePerTypeWithTenantIdNotSaved or Shape.TablePerTypeDeletedAndAdded:
                db.Add(new Dog { Id = 1, Archived = archived, Detail = $"{tenant} text" });
                break;
            case Shape.OwnedCollectionReadThroughItsOwner:
                db.Add(new Shop { Id = 1, Archived = archived, Counters = { new Counter { Id = 1, Notes = { new Note { Id = 1, Text = $"{tenant} text" } } } } });
                break;
            default:
                db.Add(new Mall { Id = 1, Archived = archived, Address = new Address { Notes = { new Note { Id = 1, Text = $"{tenant} text" } } } });
                break;
        }

        await db.SaveChangesAsync();
    }

    // The tenant's own row, loaded past its own filter, changed so that the save reads it.
    private static async Task ChangeLoadedAsync(DbContext db, Shape shape)
    {
        switch (shape)
        {
            case Shape.TenantIdInAnAlternateKey:
                var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync();
                order.Lines.Clear();
                order.Lines.Add(new Line { Id = 2, Text = "changed" });
                break;
            case Shape.TablePerTypeWithTenantIdNotSaved:
                (await db.Set<Dog>().IgnoreQueryFilters().SingleAsync()).Detail = "changed";
                break;
            case Shape.TablePerTypeDeletedAndAdded:
                db.Remove(await db.Set<Dog>().IgnoreQueryFilters().SingleAsync());
                db.Add(new Dog { Id = 1, Archived = true, Detail = "changed" });
                break;
            case Shape.OwnedCollectionReadThroughItsOwner:
                var notes = (await db.Set<Shop>().IgnoreQueryFilters().SingleAsync()).Counters[0].Notes;
                notes.Clear();
                notes.Add(new Note { Id = 2, Text = "changed" });
                break;
            default:
                var addressNotes = (await db.Set<Mall>().IgnoreQueryFilters().SingleAsync()).Address!.Notes;
                addressNotes.Clear();
                addressNotes.Add(new Note { Id = 2, Text = "changed" });
                break;
        }
    }

    // A stub with another tenant's key, attached as the current tenant's, changed so that the save reads its row.
    private static void ChangeThroughStub(DbContext db, Shape shape, string tenant)
    {
        switch (shape)
        {
            case Shape.TenantIdInAnAlternateKey:
                Order order = new() { Id = 1, TenantId = tenant };
                db.Attach(order);
                order.Lines.Add(new Line { Id = 2, Text = "overwritten" });
                break;
            case Shape.TablePerTypeWithTenantIdNotSaved:
                Dog dog = new() { Id = 1, TenantId = tenant, Detail = "acme text" };
                db.Attach(dog);
                dog.Detail = "overwritten";
                break;
            case Shape.TablePerTypeDeletedAndAdded:
                db.Remove(new Dog { Id = 1, TenantId = tenant, Detail = "acme text" });
                db.Add(new Dog { Id = 1, Detail = "overwritten" });
                break;
            case Shape.OwnedCollectionReadThroughItsOwner:
                Shop shop = new() { Id = 1, TenantId = tenant, Counters = { new Counter { Id = 1, TenantId = tenant } } };
                db.Attach(shop);
                shop.Counters[0].Notes.Add(new Note { Id = 2, Text = "overwritten" });
                break;
            default:
                Mall addressed = new() { Id = 1, TenantId = tenant, Address = new Address { TenantId = tenant } };
                db.Attach(addressed);
                addressed.Address.Notes.Add(new Note { Id = 2, Text = "overwritten" });
                break;
        }
    }

    private async Task<string> StoredTextAsync(Shape shape, string tenant)
    {
        await using var db = await CreateAsync(shape, _tenant.As(tenant));

        return shape switch
        {
            Shape.TenantIdInAnAlternateKey => string.Join(",", (await db.Set<Order>().IgnoreQueryFilters().AsNoTracking().SingleAsync()).Lines.Select(line => line.Text)),
            Shape.TablePerTypeWithTenantIdNotSaved or Shape.TablePerTypeDeletedAndAdded =>
                (await db.Set<Dog>().IgnoreQueryFilters().AsNoTracking().SingleAsync()).Detail,
            Shape.OwnedCollectionReadThroughItsOwner => string.Join(",", (await db.Set<Shop>().IgnoreQueryFilters().AsNoTracking().SingleAsync()).Counters[0].Notes.Select(note => note.Text)),
            _ => string.Join(",", (await db.Set<Mall>().IgnoreQueryFilters().AsNoTracking().SingleAsync()).Address!.Notes.Select(note => note.Text)),
        };
    }

    private async Task<DbContext> CreateAsync(Shape shape, TestTenantContext tenant, CommandLog? log = null)
    {
        DbContext db = shape switch
        {
            Shape.TenantIdInAnAlternateKey => new AlternateKeyContext(Options<AlternateKeyContext>(tenant, log)),
            Shape.TablePerTypeWithTenantIdNotSaved or Shape.TablePerTypeDeletedAndAdded =>
                new TablePerTypeContext(Options<TablePerTypeContext>(tenant, log)),
            Shape.OwnedCollectionReadThroughItsOwner => new OwnedCollectionContext(Options<OwnedCollectionContext>(tenant, log)),
            _ => new OwnedReferenceContext(Options<OwnedReferenceContext>(tenant, log)),
        };

        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private DbContextOptions<TContext> Options<TContext>(TestTenantContext tenant, CommandLog? log)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>(DbContextFactory.Options<TContext>(tenant, _connection));
        return log is null ? builder.Options : builder.AddInterceptors(log).Options;
    }

    // The commands a context sends, with their parameter values.
    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<(string Text, object?[] Values)> Commands { get; } = [];

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Commands.Add((command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value)]));
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value)]));
            return ValueTask.FromResult(result);
        }
    }

    public sealed class Order : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public bool Archived { get; set; }

        public List<Line> Lines { get; } = [];
    }

    public sealed class Line
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    public class Animal : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public bool Archived { get; set; }
    }

    public sealed class Dog : Animal
    {
        [MaxLength(64)]
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class Shop : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public bool Archived { get; set; }

        public List<Counter> Counters { get; } = [];
    }

    public sealed class Mall : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public bool Archived { get; set; }

        public Address? Address { get; set; }
    }

    public sealed class Counter : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Note> Notes { get; } = [];
    }

    public sealed class Address : ITenantEntity<string>
    {
        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Note> Notes { get; } = [];
    }

    public sealed class Note
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    // Owned through Id; (TenantId, Id) is an alternate key. The application's own, unnamed filter hides archived orders.
    private sealed class AlternateKeyContext(DbContextOptions<AlternateKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(order =>
            {
                order.Property(o => o.Id).ValueGeneratedNever();
                order.HasAlternateKey(o => new { o.TenantId, o.Id });
                order.HasQueryFilter(o => !o.Archived);
                order.OwnsMany(o => o.Lines, line => line.Property(l => l.Id).ValueGeneratedNever());
            });
    }

    // TenantId in the Animals table, not written after an insert, a dog's Detail in Dogs.
    private sealed class TablePerTypeContext(DbContextOptions<TablePerTypeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>(animal =>
            {
                animal.UseTptMappingStrategy().Property(a => a.Id).ValueGeneratedNever();
                animal.Property(a => a.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
                animal.HasQueryFilter(a => !a.Archived);
            });
            modelBuilder.Entity<Dog>().ToTable("Dogs");
        }
    }

    private sealed class OwnedCollectionContext(DbContextOptions<OwnedCollectionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Shop>(shop =>
            {
                shop.Property(s => s.Id).ValueGeneratedNever();
                shop.HasQueryFilter(s => !s.Archived);
                shop.OwnsMany(s => s.Counters, counter =>
                {
                    counter.Property(c => c.Id).ValueGeneratedNever();
                    counter.Property(c => c.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
                    counter.OwnsMany(c => c.Notes, note => note.Property(n => n.Id).ValueGeneratedNever());
                });
            });
    }

    private sealed class OwnedReferenceContext(DbContextOptions<OwnedReferenceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Mall>(mall =>
            {
                mall.Property(m => m.Id).ValueGeneratedNever();
                mall.HasQueryFilter(m => !m.Archived);
                mall.OwnsOne(m => m.Address, address =>
                {
                    address.ToTable("Addresses");
                    address.Property(a => a.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
                    address.OwnsMany(a => a.Notes, note => note.Property(n => n.Id).ValueGeneratedNever());
                });
            });
    }
}
