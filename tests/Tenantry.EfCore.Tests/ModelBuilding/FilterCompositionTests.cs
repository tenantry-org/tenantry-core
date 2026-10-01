using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tenantry;

namespace Tenantry.EfCore.Tests.ModelBuilding;

/// <summary>
/// <c>UseTenantry()</c> adds the tenant filter after <c>OnModelCreating</c>, so it combines with the context's own
/// filters however and wherever they are configured.
/// </summary>
public sealed class FilterCompositionTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.For("acme");

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task UnnamedOwnFilter_CombinesWithTheTenantFilter()
    {
        await using var db = await CreateAsync<UnnamedFilterContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
    }

    [Fact]
    public async Task IgnoreQueryFilters_RemovesTheOwnAndTheTenantFilter()
    {
        await using var db = await CreateAsync<UnnamedFilterContext>();
        await SeedAsync(db);

        (await db.Items.IgnoreQueryFilters().Select(item => item.Name).ToListAsync())
            .Should().BeEquivalentTo("acme active", "acme deleted", "globex active");
    }

    [Fact]
    public async Task OwnFilterWithSeveralConditions_CombinesWithTheTenantFilter()
    {
        await using var db = await CreateAsync<ComplexFilterContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
    }

    [Fact]
    public async Task OwnFilterSetTwice_TheLastOneCombinesWithTheTenantFilter()
    {
        // UseTenantry adds the tenant filter after both, so the second one cannot replace it.
        await using var db = await CreateAsync<FilterSetTwiceContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().BeEquivalentTo("acme active", "acme deleted");
    }

    [Fact]
    public async Task OwnFilterInAnEntityTypeConfiguration_CombinesWithTheTenantFilter()
    {
        await using var db = await CreateAsync<ConfigurationClassContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
    }

    [Fact]
    public async Task StampedEntity_IsVisibleThroughTheCombinedFilter()
    {
        await using var db = await CreateAsync<UnnamedFilterContext>();
        db.Items.Add(new Item { Name = "stamped" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.Items.SingleAsync()).TenantId.Should().Be("acme");
    }

#if EFCORE10_OR_GREATER
    [Fact]
    public async Task WithoutOwnFilters_TheTenantFilterIsNamed()
    {
        await using var db = await CreateAsync<NoOwnFilterContext>();

        db.Model.FindEntityType(typeof(Item))!.GetDeclaredQueryFilters()
            .Should().ContainSingle().Which.Key.Should().Be(TenantryQueryFilters.Tenant);
    }

    [Fact]
    public async Task NamedOwnFilter_KeepsTheTenantFilterSeparate_SoEachCanBeIgnoredAlone()
    {
        await using var db = await CreateAsync<NamedFilterContext>();
        await SeedAsync(db);

        (await db.Items.Select(item => item.Name).ToListAsync()).Should().Equal("acme active");
        (await db.Items.IgnoreQueryFilters([TenantryQueryFilters.Tenant]).Select(item => item.Name).ToListAsync())
            .Should().BeEquivalentTo("acme active", "globex active");
        (await db.Items.IgnoreQueryFilters(["SoftDelete"]).Select(item => item.Name).ToListAsync())
            .Should().BeEquivalentTo("acme active", "acme deleted");
    }

    [Fact]
    public async Task UnnamedOwnFilter_TheTenantFilterIsMergedIntoIt_AndThatIsLoggedOnce()
    {
        ListLogger logger = new();
        var services = DbContextFactory.Services<string>(
            _tenant,
            configure: collection => collection.AddLogging(logging => logging.AddProvider(logger)));
        var options = new DbContextOptionsBuilder<UnnamedFilterContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry()
            .Options;

        // A context type of this test's own, so this test builds its model.
        await using var db = new LoggedUnnamedFilterContext(options);
        _ = db.Model;
        _ = new LoggedUnnamedFilterContext(options).Model;

        db.Model.FindEntityType(typeof(Item))!.GetDeclaredQueryFilters().Should().ContainSingle().Which.Key.Should().BeNull();
        logger.Messages.Should().ContainSingle(message => message.Contains("'Item' has an unnamed query filter"));
        logger.Events.Should().ContainSingle(e => e.Id == 2004).Which.Name.Should().Be("TenantFilterMerged");
    }
#endif

    private async Task<TContext> CreateAsync<TContext>()
        where TContext : DbContext
    {
        var db = (TContext)Activator.CreateInstance(typeof(TContext), DbContextFactory.Options<TContext>(_tenant, _connection))!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private async Task SeedAsync(ItemsContext db)
    {
        db.Items.Add(new Item { Name = "acme active" });
        db.Items.Add(new Item { Name = "acme deleted", IsDeleted = true });
        await db.SaveChangesAsync();
        _tenant.As("globex");
        db.Items.Add(new Item { Name = "globex active" });
        await db.SaveChangesAsync();
        _tenant.As("acme");
        db.ChangeTracker.Clear();
    }

    public sealed class Item : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        public bool IsDeleted { get; set; }
    }

    // One context type per model, because EF Core caches the model per context type.
    public abstract class ItemsContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    public sealed class NoOwnFilterContext(DbContextOptions<NoOwnFilterContext> options) : ItemsContext(options);

    public class UnnamedFilterContext(DbContextOptions options) : ItemsContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
    }

    public sealed class LoggedUnnamedFilterContext(DbContextOptions options) : UnnamedFilterContext(options);

    public sealed class ComplexFilterContext(DbContextOptions<ComplexFilterContext> options) : ItemsContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted && item.Name != "" && item.Id > 0);
    }

    public sealed class FilterSetTwiceContext(DbContextOptions<FilterSetTwiceContext> options) : ItemsContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().HasQueryFilter(item => !item.IsDeleted);
            modelBuilder.Entity<Item>().HasQueryFilter(item => item.Name != "");
        }
    }

    public sealed class ConfigurationClassContext(DbContextOptions<ConfigurationClassContext> options) : ItemsContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new ItemConfiguration());

        private sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
        {
            public void Configure(EntityTypeBuilder<Item> builder) => builder.HasQueryFilter(item => !item.IsDeleted);
        }
    }

#if EFCORE10_OR_GREATER
    public sealed class NamedFilterContext(DbContextOptions<NamedFilterContext> options) : ItemsContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Item>().HasQueryFilter("SoftDelete", item => !item.IsDeleted);
    }

    private sealed class ListLogger : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];

        public List<EventId> Events { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
                Events.Add(eventId);
            }
        }
    }
#endif
}
