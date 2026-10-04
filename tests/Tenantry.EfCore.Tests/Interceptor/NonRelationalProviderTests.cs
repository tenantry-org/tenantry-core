using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// A provider that is not relational (EF Core's in-memory database) maps no tables, so the checks that need them do not
/// run, and adding, changing and deleting tenant-owned entities still works.
/// </summary>
public sealed class NonRelationalProviderTests
{
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();
    private readonly string _database = Guid.NewGuid().ToString();

    [Fact]
    public async Task TenantOwnedEntities_AreAddedChangedAndDeleted()
    {
        await using (var db = Create(_tenant.As("acme")))
        {
            db.Notes.Add(new Note { Id = 1, Text = "acme note" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = Create(_tenant.As("acme")))
        {
            var note = await db.Notes.SingleAsync(TestContext.Current.CancellationToken);
            note.Text = "changed";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = Create(_tenant.As("globex")))
        {
            (await db.Notes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        }

        await using (var db = Create(_tenant.As("acme")))
        {
            var note = await db.Notes.SingleAsync(TestContext.Current.CancellationToken);
            note.Text.Should().Be("changed");
            db.Notes.Remove(note);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            (await db.Notes.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        }
    }

    [Fact]
    public async Task EntitiesWhoseNamesWouldShareATable_AreNotChecked_AsTheProviderHasNone()
    {
        // The in-memory provider names a type's "table" after its class, so these two would seem to share one.
        await using var db = new SameNameContext(new DbContextOptionsBuilder<SameNameContext>()
            .UseInMemoryDatabase(_database)
            .UseApplicationServiceProvider(DbContextFactory.Services(_tenant.As("acme")))
            .UseTenantry()
            .Options);

        await db.Awaiting(context => context.Set<Archive.Note>().ToListAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task AnOwnerItsOwnFilterHides_WhoseStoredTenantIsRead_CanBeGivenOwnedEntities_ButAnotherTenantsCannot()
    {
        // Keyed on (TenantId, Id), the owner's TenantId is not written back: its stored row is read, past the
        // application's filter.
        await using (var db = CreateOrders(_tenant.As("acme")))
        {
            db.Add(new Order { Id = 1, Archived = true, Lines = { new Line { Id = 1, Text = "acme line" } } });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = CreateOrders(_tenant.As("acme")))
        {
            (await db.Set<Order>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken)).Lines.Add(new Line { Id = 2, Text = "added" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = CreateOrders(_tenant.As("globex")))
        {
            Order stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            stub.Lines.Add(new Line { Id = 3, Text = "from globex" });

            await db.Awaiting(context => context.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        await using (var db = CreateOrders(_tenant.As("acme")))
        {
            (await db.Set<Order>().IgnoreQueryFilters().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Lines
                .Select(line => line.Text).Should().BeEquivalentTo("acme line", "added");
        }
    }

    private OrdersContext CreateOrders(TestTenantContext tenant) =>
        new(new DbContextOptionsBuilder<OrdersContext>()
            .UseInMemoryDatabase(_database)
            .UseApplicationServiceProvider(DbContextFactory.Services(tenant))
            .UseTenantry()
            .Options);

    private NotesContext Create(TestTenantContext tenant) =>
        new(new DbContextOptionsBuilder<NotesContext>()
            .UseInMemoryDatabase(_database)
            .UseApplicationServiceProvider(DbContextFactory.Services(tenant))
            .UseTenantry()
            .Options);

    public sealed class Note : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class NotesContext(DbContextOptions<NotesContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();
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

    private sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
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

    // The DbSet properties name the entity types for EF Core, which reads them by reflection.
    // ReSharper disable UnusedMember.Local
    private sealed class SameNameContext(DbContextOptions<SameNameContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        public DbSet<Archive.Note> ArchivedNotes => Set<Archive.Note>();
    }
    // ReSharper restore UnusedMember.Local

    public static class Archive
    {
        // The same name as the other Note on purpose: the test is about two entity types of one name.
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public sealed class Note
        {
            public int Id { get; set; }
        }
    }
}
