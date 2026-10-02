using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// An owner whose <c>TenantId</c> is part of a key, or not written after an insert. When its owned entities are owned
/// through a key with its <c>TenantId</c>, their foreign key names the tenant; otherwise its <c>TenantId</c> cannot be
/// written back (EF Core does not let a key change), so its stored tenant is read before the save. Either way, adding
/// to another tenant's owner through a stub must fail and write nothing.
/// </summary>
public sealed class KeyedOwnerTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    public static TheoryData<Shape, bool> Shapes() => new()
    {
        { Shape.TenantIdInAnAlternateKey, false },
        { Shape.TenantIdInAnAlternateKey, true },
        { Shape.TenantIdInThePrimaryKey, false },
        { Shape.TenantIdInThePrimaryKey, true },
        { Shape.OwnedThroughAnAlternateKey, false },
        { Shape.OwnedThroughAnAlternateKey, true },
        { Shape.TenantIdNotSavedAfterInsert, false },
        { Shape.TenantIdNotSavedAfterInsert, true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task AddingToTheCurrentTenantsLoadedOwner_Works(Shape shape, bool sync)
    {
        await SeedAcmeOrderAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("acme")))
        {
            var order = await db.Set<Order>().SingleAsync(TestContext.Current.CancellationToken);
            order.Lines.Add(new Line { Id = 2, Text = "second acme line" });
            await SaveAsync(db, sync);
        }

        (await AcmeLinesAsync(shape)).Should().Equal("acme line", "second acme line");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task AddingToAnotherTenantsOwner_ThroughAStubNamingThatTenant_IsRejected(Shape shape, bool sync)
    {
        await SeedAcmeOrderAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("globex")))
        {
            Order stub = new() { Id = 1, TenantId = "acme" };
            db.Attach(stub);
            stub.Lines.Add(new Line { Id = 2, Text = "from globex" });

            (await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.OffendingTenantId.Should().Be("acme");
        }

        (await AcmeLinesAsync(shape)).Should().Equal("acme line");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task AddingToAnotherTenantsOwner_ThroughAStubWithAForgedTenantId_WritesNothingTheOwnerReads(Shape shape, bool sync)
    {
        // Owned through Id alone, the stub's stored tenant is read and the save is rejected. Owned through a key with
        // TenantId, the new line names the stub's (globex, 1), which is no one's order: the database's foreign key
        // rejects it, or, without one, no order reads it.
        await SeedAcmeOrderAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("globex")))
        {
            Order stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            stub.Lines.Add(new Line { Id = 2, Text = "from globex" });

            if (shape is Shape.TenantIdInAnAlternateKey or Shape.TenantIdNotSavedAfterInsert)
            {
                (await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>())
                    .WithMessage("The 'Order' that owns entities being saved is not stored for the current tenant 'globex'*");
            }
            else
            {
                await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateException>();
            }
        }

        (await AcmeLinesAsync(shape)).Should().Equal("acme line");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task NewOwnersWithOwnedEntities_AreSavedTogether(Shape shape, bool sync)
    {
        // A new owner keyed by its TenantId gets it, and its owned entities their foreign key, when it is stamped.
        await using (var db = await CreateAsync(shape, _tenant.As("acme")))
        {
            db.Set<Order>().Add(new Order { Id = 1, Lines = { new Line { Id = 1, Text = "acme line" } } });
            db.Set<Order>().Add(new Order { Id = 2, Lines = { new Line { Id = 2, Text = "second order's line" } } });
            await SaveAsync(db, sync);
        }

        await using var acme = await CreateAsync(shape, _tenant.As("acme"));
        (await acme.Set<Order>().AsNoTracking().OrderBy(order => order.Id).ToListAsync(TestContext.Current.CancellationToken))
            .Select(order => order.Lines.Single().Text).Should().Equal("acme line", "second order's line");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task AddingToAnotherTenantsOwner_ThroughAStubMarkedModified_WritesNothingTheOwnerReads(Shape shape, bool sync)
    {
        // A modified owner with nothing EF Core writes sends no UPDATE, so its own statement cannot carry the check.
        await SeedAcmeOrderAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("globex")))
        {
            Order stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            db.Entry(stub).State = EntityState.Modified;
            stub.Lines.Add(new Line { Id = 2, Text = "from globex" });

            if (shape is Shape.TenantIdInAnAlternateKey or Shape.TenantIdNotSavedAfterInsert)
            {
                await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>();
            }
            else
            {
                await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateException>();
            }
        }

        (await AcmeLinesAsync(shape)).Should().Equal("acme line");
    }

    [Fact]
    public async Task ReadingTheStoredTenant_HappensOncePerOwner_AndOnlyWhenItsTenantIdCannotBeWrittenBack()
    {
        await SeedAcmeOrderAsync(Shape.TenantIdInAnAlternateKey);
        QueryCounter counter = new();

        await using var db = await CreateAsync(Shape.TenantIdInAnAlternateKey, _tenant.As("acme"), counter);
        var order = await db.Set<Order>().SingleAsync(TestContext.Current.CancellationToken);
        order.Lines.Add(new Line { Id = 2, Text = "second" });
        order.Lines.Add(new Line { Id = 3, Text = "third" });
        counter.Reads = 0;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        counter.Reads.Should().Be(1);
        order.Lines[0].Text = "changed";
        counter.Reads = 0;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        counter.Reads.Should().Be(0, "a change to an owned entity with a TenantId of its own checks the entity itself");
    }

    public enum Shape
    {
        TenantIdInAnAlternateKey,
        TenantIdInThePrimaryKey,
        OwnedThroughAnAlternateKey,
        TenantIdNotSavedAfterInsert,
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

    private async Task SeedAcmeOrderAsync(Shape shape)
    {
        await using var db = await CreateAsync(shape, _tenant.As("acme"));
        db.Set<Order>().Add(new Order { Id = 1, Lines = { new Line { Id = 1, Text = "acme line" } } });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> AcmeLinesAsync(Shape shape)
    {
        await using var db = await CreateAsync(shape, _tenant.As("acme"));
        var order = await db.Set<Order>().AsNoTracking().SingleAsync();
        return [.. order.Lines.OrderBy(line => line.Id).Select(line => line.Text)];
    }

    private async Task<DbContext> CreateAsync(Shape shape, TestTenantContext tenant, QueryCounter? counter = null)
    {
        DbContext db = shape switch
        {
            Shape.TenantIdInAnAlternateKey => new AlternateKeyContext(Options<AlternateKeyContext>(tenant, counter)),
            Shape.TenantIdInThePrimaryKey => new PrimaryKeyContext(Options<PrimaryKeyContext>(tenant, counter)),
            Shape.OwnedThroughAnAlternateKey => new OwnedThroughAlternateKeyContext(Options<OwnedThroughAlternateKeyContext>(tenant, counter)),
            _ => new TenantIdNotSavedContext(Options<TenantIdNotSavedContext>(tenant, counter)),
        };

        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private DbContextOptions<TContext> Options<TContext>(TestTenantContext tenant, QueryCounter? counter)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>(DbContextFactory.Options<TContext>(tenant, _connection));
        return counter is null ? builder.Options : builder.AddInterceptors(counter).Options;
    }

    // Counts the queries a save runs.
    private sealed class QueryCounter : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public int Reads { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Reads++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    public sealed class Order : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Line> Lines { get; } = [];
    }

    public sealed class Line : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Text { get; set; } = string.Empty;
    }

    // Owned through Id; (TenantId, Id) is an alternate key, as a composite foreign key from another entity needs.
    private sealed class AlternateKeyContext(DbContextOptions<AlternateKeyContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(order =>
            {
                order.Property(o => o.Id).ValueGeneratedNever();
                order.HasAlternateKey(o => new { o.TenantId, o.Id });
                order.OwnsMany(o => o.Lines, line =>
                {
                    line.HasKey(l => l.Id);
                    line.Property(l => l.Id).ValueGeneratedNever();
                });
            });
    }

    // Keyed and owned through (TenantId, Id).
    private sealed class PrimaryKeyContext(DbContextOptions<PrimaryKeyContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(order =>
            {
                order.HasKey(o => new { o.TenantId, o.Id });
                order.Property(o => o.Id).ValueGeneratedNever();
                order.OwnsMany(o => o.Lines, line =>
                {
                    line.HasKey(l => l.Id);
                    line.Property(l => l.Id).ValueGeneratedNever();
                });
            });
    }

    // TenantId is not written after an insert (a way to make it immutable), so it cannot be written back either.
    private sealed class TenantIdNotSavedContext(DbContextOptions<TenantIdNotSavedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(order =>
            {
                order.Property(o => o.Id).ValueGeneratedNever();
                order.Property(o => o.TenantId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
                order.OwnsMany(o => o.Lines, line =>
                {
                    line.HasKey(l => l.Id);
                    line.Property(l => l.Id).ValueGeneratedNever();
                });
            });
    }

    // Keyed by Id, owned through the alternate key (TenantId, Id).
    private sealed class OwnedThroughAlternateKeyContext(DbContextOptions<OwnedThroughAlternateKeyContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Order>(order =>
            {
                order.Property(o => o.Id).ValueGeneratedNever();
                order.OwnsMany(o => o.Lines, line =>
                {
                    line.WithOwner().HasForeignKey("OrderTenantId", "OrderId").HasPrincipalKey(o => new { o.TenantId, o.Id });
                    line.HasKey(l => l.Id);
                    line.Property(l => l.Id).ValueGeneratedNever();
                });
            });
    }
}
