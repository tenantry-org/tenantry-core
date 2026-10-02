using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// An entity mapped to more than one table (TPT, entity splitting) is updated only in the tables whose columns changed,
/// and only the table with <c>TenantId</c> checks its concurrency token. A change to another table's columns, through a
/// stub with a forged <c>TenantId</c>, must still fail and write nothing.
/// </summary>
public sealed class MultiTableEntityTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    public static TheoryData<Shape, bool> Shapes() => new()
    {
        { Shape.TablePerType, false },
        { Shape.TablePerType, true },
        { Shape.TablePerTypeWithTenantIdNotSaved, false },
        { Shape.TablePerTypeWithTenantIdNotSaved, true },
        { Shape.EntitySplitting, false },
        { Shape.EntitySplitting, true },
        { Shape.TablePerConcreteType, false },
        { Shape.TablePerConcreteType, true },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ChangingAnotherTenantsRow_InATableWithoutTenantId_ThroughAStub_FailsAndWritesNothing(Shape shape, bool sync)
    {
        await SeedAcmeAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("globex")))
        {
            Dog stub = new() { Id = 1, TenantId = "globex", Detail = "acme detail" };
            db.Attach(stub);
            stub.Detail = "overwritten by globex";

            if (shape == Shape.TablePerTypeWithTenantIdNotSaved)
            {
                (await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>())
                    .WithMessage("*'Dog': no row with its key is stored for the current tenant 'globex'*");
            }
            else
            {
                await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();
            }
        }

        (await AcmeDetailAsync(shape)).Should().Be("acme detail");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ChangingTheCurrentTenantsRow_InATableWithoutTenantId_Works(Shape shape, bool sync)
    {
        await SeedAcmeAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("acme")))
        {
            var dog = await db.Set<Dog>().SingleAsync(TestContext.Current.CancellationToken);
            dog.Detail = "changed";
            await SaveAsync(db, sync);
        }

        (await AcmeDetailAsync(shape)).Should().Be("changed");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ReplacingAnotherTenantsRow_ThroughAStubDeletedAndAddedAgain_IsRejected(Shape shape, bool sync)
    {
        // EF Core saves the pair as an UPDATE of what differs, table by table: here only Detail, without TenantId's table
        // when they are different tables.
        await SeedAcmeAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("globex")))
        {
            db.Remove(new Dog { Id = 1, TenantId = "globex", Detail = "acme detail" });
            db.Add(new Dog { Id = 1, Detail = "overwritten by globex" });

            // On one table, the UPDATE EF Core sends carries the deleted one's TenantId token.
            if (shape == Shape.TablePerConcreteType)
            {
                await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();
            }
            else
            {
                (await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>())
                    .WithMessage("*'Dog': no row with its key is stored for the current tenant 'globex'*");
            }
        }

        (await AcmeDetailAsync(shape)).Should().Be("acme detail");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task ReplacingTheCurrentTenantsRow_WithOneUnderTheSameKey_Works(Shape shape, bool sync)
    {
        await SeedAcmeAsync(shape);

        await using (var db = await CreateAsync(shape, _tenant.As("acme")))
        {
            db.Remove(new Dog { Id = 1, TenantId = "acme", Detail = "acme detail" });
            db.Add(new Dog { Id = 1, Detail = "replaced" });
            await SaveAsync(db, sync);
        }

        (await AcmeDetailAsync(shape)).Should().Be("replaced");
    }

    [Fact]
    public async Task AnEntityKeyedByItsTenantId_IsUpdatedWithoutAReadOfItsRow_EvenOneItsOwnFilterHides()
    {
        // Every table's key includes TenantId, so each UPDATE names the tenant: a read would add nothing, and goes
        // through the application's own filter, which hides an archived row.
        await using (var db = await CreateKeyedAsync(_tenant.As("acme")))
        {
            db.Add(new KeyedDog { Id = 1, Detail = "acme detail", Archived = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await CreateKeyedAsync(_tenant.As("acme")))
        {
            var dog = await db.Set<KeyedDog>().IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
            dog.Detail = "changed";
            dog.Archived = false;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = await CreateKeyedAsync(_tenant.As("globex")))
        {
            KeyedDog stub = new() { Id = 1, TenantId = "globex", Detail = "changed" };
            db.Attach(stub);
            stub.Detail = "overwritten by globex";
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        await using var acme = await CreateKeyedAsync(_tenant.As("acme"));
        (await acme.Set<KeyedDog>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Detail.Should().Be("changed");
    }

    public enum Shape
    {
        TablePerType,
        TablePerTypeWithTenantIdNotSaved,
        EntitySplitting,

        // Each concrete type's table has its own TenantId, so this needs nothing more: the control.
        TablePerConcreteType,
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

    private async Task SeedAcmeAsync(Shape shape)
    {
        await using var db = await CreateAsync(shape, _tenant.As("acme"));
        db.Add(new Dog { Id = 1, Detail = "acme detail" });
        await db.SaveChangesAsync();
    }

    private async Task<string> AcmeDetailAsync(Shape shape)
    {
        await using var db = await CreateAsync(shape, _tenant.As("acme"));
        return (await db.Set<Dog>().AsNoTracking().SingleAsync()).Detail;
    }

    private async Task<DbContext> CreateAsync(Shape shape, TestTenantContext tenant)
    {
        DbContext db = shape switch
        {
            Shape.TablePerType => new TablePerTypeContext(DbContextFactory.Options<TablePerTypeContext>(tenant, _connection)),
            Shape.TablePerTypeWithTenantIdNotSaved => new TenantIdNotSavedContext(DbContextFactory.Options<TenantIdNotSavedContext>(tenant, _connection)),
            Shape.EntitySplitting => new SplitContext(DbContextFactory.Options<SplitContext>(tenant, _connection)),
            _ => new TablePerConcreteTypeContext(DbContextFactory.Options<TablePerConcreteTypeContext>(tenant, _connection)),
        };

        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private async Task<KeyedContext> CreateKeyedAsync(TestTenantContext tenant)
    {
        KeyedContext db = new(DbContextFactory.Options<KeyedContext>(tenant, _connection));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public class KeyedAnimal : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public bool Archived { get; set; }
    }

    public sealed class KeyedDog : KeyedAnimal
    {
        [MaxLength(64)]
        public string Detail { get; set; } = string.Empty;
    }

    // Table-per-type, keyed by (TenantId, Id), with a filter of the application's own that hides archived animals.
    private sealed class KeyedContext(DbContextOptions<KeyedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<KeyedAnimal>(animal =>
            {
                animal.UseTptMappingStrategy().ToTable("KeyedAnimals");
                animal.HasKey(a => new { a.TenantId, a.Id });
                animal.Property(a => a.Id).ValueGeneratedNever();
                animal.HasQueryFilter(a => !a.Archived);
            });
            modelBuilder.Entity<KeyedDog>().ToTable("KeyedDogs");
        }
    }

    public class Animal : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Dog : Animal
    {
        [MaxLength(64)]
        public string Detail { get; set; } = string.Empty;
    }

    // Dog's Detail is in the Dogs table, TenantId in the Animals table.
    private sealed class TablePerTypeContext(DbContextOptions<TablePerTypeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>().UseTptMappingStrategy().Property(animal => animal.Id).ValueGeneratedNever();
            modelBuilder.Entity<Dog>().ToTable("Dogs");
        }
    }

    // TenantId is not written after an insert, so it cannot be written back: the stored row is read instead.
    private sealed class TenantIdNotSavedContext(DbContextOptions<TenantIdNotSavedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>(animal =>
            {
                animal.UseTptMappingStrategy().Property(a => a.Id).ValueGeneratedNever();
                animal.Property(a => a.TenantId).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            });
            modelBuilder.Entity<Dog>().ToTable("Dogs");
        }
    }

    // One entity type over two tables: Detail in DogDetails, TenantId in Dogs.
    private sealed class SplitContext(DbContextOptions<SplitContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Animal>();
            modelBuilder.Entity<Dog>(dog =>
            {
                dog.Property(d => d.Id).ValueGeneratedNever();
                dog.SplitToTable("DogDetails", table => table.Property(d => d.Detail));
            });
        }
    }

    private sealed class TablePerConcreteTypeContext(DbContextOptions<TablePerConcreteTypeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Animal>().UseTpcMappingStrategy().Property(animal => animal.Id).ValueGeneratedNever();
            modelBuilder.Entity<Dog>().ToTable("Dogs");
        }
    }
}
