using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// The isolation's log messages have stable event ids under the category <c>Tenantry.EfCore</c>, so applications can
/// alert on them: an isolation violation above all.
/// </summary>
public sealed class IsolationLogTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    // The test tenant is per async flow, so each test sets it in its own.
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();
    private readonly Recorder _logs = new();

    [Fact]
    public async Task AnIsolationViolation_IsLoggedAsEvent2001()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync();
        db.Orders.Add(new Order { Description = "Acme order" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var id = db.Orders.Single().Id;
        db.ChangeTracker.Clear();

        _tenant.As("globex");
        Order forged = new() { Id = id, TenantId = "acme", Description = "Forged" };
        db.Orders.Attach(forged);
        db.Entry(forged).State = EntityState.Modified;

        await db.Invoking(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();

        var entry = _logs.Entries.Should().ContainSingle(e => e.EventId.Id == 2001).Subject;
        entry.Category.Should().Be("Tenantry.EfCore");
        entry.Level.Should().Be(LogLevel.Error);
        entry.EventId.Name.Should().Be("TenantIsolationViolation");
        entry.Message.Should().Contain("'acme'").And.Contain("'globex'");
    }

    [Fact]
    public async Task AWriteWithoutATenant_UnderWarn_IsLoggedAsEvent2002()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync(new EfCoreIsolationOptions { OnMissingTenant = MissingTenantBehavior.Warn });
        Order order = new() { Description = "Acme order" };
        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _tenant.AsNone();
        order.Description = "Changed";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _logs.Entries.Should().ContainSingle(e => e.EventId.Id == 2002)
            .Which.Should().BeEquivalentTo(new { Category = "Tenantry.EfCore", Level = LogLevel.Warning });
    }

    [Fact]
    public async Task AWriteThatMatchesNoRow_IsLoggedAsEvent2003()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync();
        Order missing = new() { Id = 404, TenantId = "acme", Description = "Not there" };
        db.Orders.Attach(missing);
        db.Entry(missing).State = EntityState.Modified;

        await db.Invoking(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

        _logs.Entries.Should().ContainSingle(e => e.EventId.Id == 2003)
            .Which.Should().BeEquivalentTo(new { Category = "Tenantry.EfCore", Level = LogLevel.Warning });
    }

    [Fact]
    public async Task UnmarkedEntityTypes_UnderWarn_AreLoggedAsEvent2006_OncePerModel()
    {
        _tenant.As("acme");
        var services = DbContextFactory.Services(
            _tenant,
            new EfCoreIsolationOptions { OnUnmarkedEntityType = UnmarkedEntityTypeBehavior.Warn },
            collection => collection.AddLogging(logging => logging.AddProvider(_logs)));

        var options = new DbContextOptionsBuilder<UnmarkedContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry()
            .Options;
        HashSet<object> models = [];

        for (var i = 0; i < 2; i++)
        {
            await using UnmarkedContext db = new(options);
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            (await db.Invoices.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
            models.Add(db.Model);
        }

        // Once for each model EF Core built: it can drop a model from its cache under the other tests' load and build
        // it again, as it can in an application.
        var entries = _logs.Entries.Where(e => e.EventId.Id == 2006).ToList();
        entries.Should().HaveCount(models.Count);
        var entry = entries[0];
        entry.Should().BeEquivalentTo(new { Category = "Tenantry.EfCore", Level = LogLevel.Warning });
        entry.EventId.Name.Should().Be("UnmarkedEntityTypes");
        entry.Message.Should().Contain("Invoice").And.Contain("UnmarkedContext");
    }

    [Fact]
    public async Task StringTenantIdsOnSqlite_AreNotLoggedAsEvent2007()
    {
        // SQLite's default collation, BINARY, compares ids exactly. The context type is this test's own, so its model is
        // built here.
        _tenant.As("acme");
        await using SqliteOrdersContext db = new(new DbContextOptionsBuilder<SqliteOrdersContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(DbContextFactory.Services(
                _tenant, configure: collection => collection.AddLogging(logging => logging.AddProvider(_logs))))
            .UseTenantry()
            .Options);

        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        (await db.Orders.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        _logs.Entries.Should().NotContain(e => e.EventId.Id == 2007);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private async Task<TestDbContext> CreateAsync(EfCoreIsolationOptions? isolation = null)
    {
        var services = DbContextFactory.Services(
            _tenant,
            isolation,
            collection => collection.AddLogging(logging => logging.AddProvider(_logs)));
        TestDbContext db = new(new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry()
            .Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public sealed class Invoice
    {
        public int Id { get; set; }
    }

    private sealed class UnmarkedContext(DbContextOptions<UnmarkedContext> options) : DbContext(options)
    {
        // EF Core reads the DbSet property to find the entity type.
        // ReSharper disable once UnusedMember.Local
        public DbSet<Order> Orders => Set<Order>();

        public DbSet<Invoice> Invoices => Set<Invoice>();
    }

    private sealed class SqliteOrdersContext(DbContextOptions<SqliteOrdersContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    private sealed class Recorder : ILoggerProvider
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
