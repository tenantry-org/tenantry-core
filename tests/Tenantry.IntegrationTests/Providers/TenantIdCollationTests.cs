using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Tenantry.EfCore;

namespace Tenantry.IntegrationTests.Providers;

/// <summary>
/// With <c>string</c> tenant ids, the database compares <c>TenantId</c> under the column's collation, and SQL Server's
/// and MySQL's defaults ignore case: event 2007 names the tables a model leaves to that default. Each test builds a
/// model of its own context type, without connecting, so the model is built under its own application's services.
/// </summary>
public sealed class TenantIdCollationTests
{
    private const string SqlServer = "Server=unused;Database=unused";

    [Fact]
    public void OnSqlServer_StringTenantIdsWithoutACollation_AreLoggedAsEvent2007_NamingEachTableOnce()
    {
        var entry = Warnings<UncollatedContext, string>(options => options.UseSqlServer(SqlServer)).Should().ContainSingle().Subject;

        entry.Category.Should().Be("Tenantry.EfCore");
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Name.Should().Be("StringTenantIdCollation");
        // TPH under its root's table; TPT in the root's table only, which has the column; TPC in each concrete table;
        // an owned type in its own table and, in its owner's, once; a view and a shared entity's TenantId not at all.
        entry.Message.Should()
            .StartWith("'UncollatedContext' has string tenant ids in tables whose TenantId column has no collation: " +
                       "Animal, Basket, Circles, Lines, Squares, Vehicle, sales.Invoices.")
            .And.Contain("IgnoreWarnings(TenantryWarnings.StringTenantIdCollation)");
    }

    [Fact]
    public void ACollationOnTheColumn_SilencesItsTable()
    {
        var entry = Warnings<ColumnCollationContext, string>(options => options.UseSqlServer(SqlServer)).Should().ContainSingle().Subject;

        entry.Message.Should().Contain(": Animal.").And.NotContain("Basket");
    }

    [Fact]
    public void UnderEntitySplitting_TheTableThatHasTheTenantIdColumn_IsNamed()
    {
        var entry = Warnings<SplitContext, string>(options => options.UseSqlServer(SqlServer)).Should().ContainSingle().Subject;

        entry.Message.Should().Contain(": OrderTenants.");
    }

    [Fact]
    public void ACollationOnTheModel_SilencesEveryTable() =>
        Warnings<ModelCollationContext, string>(options => options.UseSqlServer(SqlServer)).Should().BeEmpty();

    [Fact]
    public void AStringTenantIdStoredAsBytes_IsNotComparedAsText() =>
        Warnings<BinaryTenantIdContext, string>(options => options.UseSqlServer(SqlServer)).Should().BeEmpty();

    [Fact]
    public void GuidTenantIds_EvenStoredAsText_AreNotWarnedAbout() =>
        Warnings<GuidTenantIdContext, Guid>(options => options.UseSqlServer(SqlServer)).Should().BeEmpty();

    [Fact]
    public void IntTenantIds_AreNotWarnedAbout() =>
        Warnings<IntTenantIdContext, int>(options => options.UseSqlServer(SqlServer)).Should().BeEmpty();

    [Fact]
    public void AContextWithoutTenantOwnedEntities_IsNotWarnedAbout() =>
        Warnings<SharedOnlyContext, string>(options => options.UseSqlServer(SqlServer)).Should().BeEmpty();

    [Fact]
    public void OnPostgreSql_WhoseDefaultComparesExactly_NothingIsLogged() =>
        Warnings<PostgreSqlContext, string>(options => options.UseNpgsql("Host=unused")).Should().BeEmpty();

    [Fact]
    public void AnApplicationThatIgnoresTheWarning_LogsNothing() =>
        Warnings<IgnoredContext, string>(
            options => options.UseSqlServer(SqlServer),
            tenant => tenant.IgnoreWarnings(TenantryWarnings.StringTenantIdCollation)).Should().BeEmpty();

    [Fact]
    public void ItIsLoggedOncePerModel()
    {
        var services = Services<string>();
        HashSet<object> models = [];

        for (var i = 0; i < 3; i++)
        {
            using OnceContext db = new(new DbContextOptionsBuilder<OnceContext>()
                .UseSqlServer(SqlServer)
                .UseApplicationServiceProvider(services)
                .UseTenantry()
                .Options);
            models.Add(db.Model);
        }

        // Once for each model EF Core built: it can drop a model from its cache under load and build it again.
        services.GetRequiredService<Recorder>().Entries.Where(e => e.EventId.Id == 2007).Should().HaveCount(models.Count);
    }

    [Fact]
    public void WithoutLogging_TheModelIsBuilt()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>();
        using UnloggedContext db = new(new DbContextOptionsBuilder<UnloggedContext>()
            .UseSqlServer(SqlServer)
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .UseTenantry()
            .Options);

        db.Model.FindEntityType(typeof(Order)).Should().NotBeNull();
    }

    /// <summary>The event 2007 entries logged while building <typeparamref name="TContext"/>'s model.</summary>
    internal static List<Recorder.Entry> Warnings<TContext, TKey>(
        Func<DbContextOptionsBuilder<TContext>, DbContextOptionsBuilder<TContext>> provider,
        Action<ITenantBuilder<TKey>>? configure = null)
        where TContext : DbContext
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        var services = Services(configure);
        var options = provider(new DbContextOptionsBuilder<TContext>()).UseApplicationServiceProvider(services).UseTenantry().Options;

        using var db = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        db.Model.Should().NotBeNull();

        return services.GetRequiredService<Recorder>().Entries.Where(e => e.EventId.Id == 2007).ToList();
    }

    private static ServiceProvider Services<TKey>(Action<ITenantBuilder<TKey>>? configure = null)
        where TKey : IEquatable<TKey>, IParsable<TKey>
    {
        Recorder recorder = new();
        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddLogging(logging => logging.AddProvider(recorder));
        services.AddTenantry(configure);
        return services.BuildServiceProvider();
    }

    public sealed class Order : TenantEntity<string>
    {
        public int Id { get; set; }
    }

    public sealed class SplitOrder : TenantEntity<string>
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class Invoice : TenantEntity<string>
    {
        public int Id { get; set; }
    }

    public class Animal : TenantEntity<string>
    {
        public int Id { get; set; }
    }

    public sealed class Dog : Animal;

    public class Vehicle : TenantEntity<string>
    {
        public int Id { get; set; }
    }

    public sealed class Car : Vehicle;

    public abstract class Shape : TenantEntity<string>
    {
        public int Id { get; set; }
    }

    public sealed class Circle : Shape;

    public sealed class Square : Shape;

    public sealed class Basket : TenantEntity<string>
    {
        public int Id { get; set; }

        public List<Line> Lines { get; } = [];

        public Line? Featured { get; set; }
    }

    public sealed class Line : TenantEntity<string>
    {
        public int Quantity { get; set; }
    }

    public sealed class Summary : TenantEntity<string>
    {
        public int Count { get; set; }
    }

    // Shared by every tenant, with a TenantId of its own that Tenantry does not compare.
    [SharedAcrossTenants]
    public sealed class Country
    {
        public int Id { get; set; }

        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class GuidOrder : TenantEntity<Guid>
    {
        public int Id { get; set; }
    }

    public sealed class IntOrder : TenantEntity<int>
    {
        public int Id { get; set; }
    }

    /// <summary>A context whose model is built by <paramref name="configure"/>.</summary>
    public abstract class ModelContext(DbContextOptions options, Action<ModelBuilder> configure) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);
    }

    private sealed class UncollatedContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
    {
        modelBuilder.Entity<Invoice>().ToTable("Invoices", "sales");
        modelBuilder.Entity<Animal>();
        modelBuilder.Entity<Dog>();
        modelBuilder.Entity<Vehicle>().UseTptMappingStrategy();
        modelBuilder.Entity<Car>().ToTable("Cars");
        modelBuilder.Entity<Shape>().UseTpcMappingStrategy().Property(shape => shape.Id).ValueGeneratedNever();
        modelBuilder.Entity<Circle>().ToTable("Circles");
        modelBuilder.Entity<Square>().ToTable("Squares");
        modelBuilder.Entity<Basket>(basket =>
        {
            basket.OwnsMany(b => b.Lines).ToTable("Lines");
            basket.OwnsOne(b => b.Featured);
        });
        modelBuilder.Entity<Summary>().HasNoKey().ToView("Summaries");
        modelBuilder.Entity<Country>();
    });

    private sealed class ColumnCollationContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
    {
        modelBuilder.Entity<Basket>(basket =>
        {
            basket.Property(b => b.TenantId).UseCollation("Latin1_General_BIN2");
            basket.OwnsOne(b => b.Featured).Property(line => line.TenantId).UseCollation("Latin1_General_BIN2");
            basket.Ignore(b => b.Lines);
        });
        modelBuilder.Entity<Animal>();
    });

    private sealed class SplitContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<SplitOrder>()
            .ToTable("Orders")
            .SplitToTable("OrderTenants", table => table.Property(order => order.TenantId)));

    private sealed class ModelCollationContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
    {
        modelBuilder.UseCollation("Latin1_General_100_CS_AS");
        modelBuilder.Entity<Order>();
    });

    private sealed class BinaryTenantIdContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<Order>().Property(order => order.TenantId).HasConversion<byte[]>());

    private sealed class GuidTenantIdContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<GuidOrder>().Property(order => order.TenantId).HasConversion<string>());

    private sealed class IntTenantIdContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<IntOrder>());

    private sealed class SharedOnlyContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<Country>());

    private sealed class PostgreSqlContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<Order>());

    private sealed class IgnoredContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<Order>());

    private sealed class OnceContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<Order>());

    private sealed class UnloggedContext(DbContextOptions options) : ModelContext(options, modelBuilder =>
        modelBuilder.Entity<Order>());

    internal sealed class Recorder : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        public sealed record Entry(string Category, EventId EventId, LogLevel Level, string Message);

        private sealed class Logger(Recorder recorder, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                recorder.Entries.Enqueue(new Entry(category, eventId, logLevel, formatter(state, exception)));
        }
    }
}
