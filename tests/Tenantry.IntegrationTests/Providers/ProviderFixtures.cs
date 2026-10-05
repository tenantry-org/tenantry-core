using System.ComponentModel.DataAnnotations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

// Every test in this assembly needs Docker: CONTRIBUTING.md shows how to run the others (`--filter "Category!=Integration"`).
[assembly: Trait("Category", "Integration")]

// One container per database for each framework's test run, shared by every test class (MySQL's is in
// MySqlProviderTests.cs). The containers start together (DatabaseContainers).
[assembly: AssemblyFixture(typeof(Tenantry.IntegrationTests.Providers.SqlServerFixture))]
[assembly: AssemblyFixture(typeof(Tenantry.IntegrationTests.Providers.PostgreSqlFixture))]

namespace Tenantry.IntegrationTests.Providers;

/// <summary>
/// One database container per provider, shared by every test class in the run. Tests use fresh tenant ids or
/// databases, so they never need a clean database.
/// </summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    protected abstract IDatabaseContainer Container { get; }

    public string ConnectionString => Container.GetConnectionString();

    public abstract DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options);

    /// <summary>The fixture's connection string, pointing at <paramref name="database"/> instead.</summary>
    public abstract string WithDatabase(string database);

    /// <summary>A quoted identifier for raw SQL: ANSI double quotes, unless the provider uses another style.</summary>
    public virtual string Quote(string identifier) => $"\"{identifier}\"";

    public async ValueTask InitializeAsync()
    {
        await DatabaseContainers.StartedAsync(Container);

        var options = UseProvider(new DbContextOptionsBuilder<ProviderOrdersContext>());
        await using ProviderOrdersContext db = new((DbContextOptions<ProviderOrdersContext>)options.Options);
        await db.Database.EnsureCreatedAsync();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Container.DisposeAsync();
    }
}

/// <summary>
/// The run's database containers, which start together when the first fixture initializes: xUnit initializes the
/// assembly fixtures one after another, and each container takes seconds to accept connections. Each fixture waits
/// only for its own container, and disposes it.
/// </summary>
internal static partial class DatabaseContainers
{
    private static readonly Lazy<Dictionary<IDatabaseContainer, Task>> Started = new(StartAll);

    // At SQL Server's minimum of 2 GB, which the tests stay well within: otherwise it sizes itself to 80% of the
    // memory Docker has, and a few runs at once exhaust it.
    public static IDatabaseContainer SqlServer { get; } = new MsSqlBuilder(ContainerImages.SqlServer)
        .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
        .Build();

    public static IDatabaseContainer PostgreSql { get; } = new PostgreSqlBuilder(ContainerImages.PostgreSql).Build();

    /// <summary>Starts every container the first time it is called; waits for <paramref name="container"/>.</summary>
    public static async Task StartedAsync(IDatabaseContainer container)
    {
        try
        {
            await Started.Value[container];
        }
        catch
        {
            // xUnit creates no further fixture once one fails, so none would dispose the containers started for them:
            // once every start has finished, successful or not, dispose them all. A fixture xUnit did create disposes
            // its container again, which Testcontainers allows.
            await Task.WhenAll(Started.Value.Select(async started =>
            {
                await Task.WhenAny(started.Value);
                await started.Key.DisposeAsync();
            }));
            throw;
        }
    }

    private static Dictionary<IDatabaseContainer, Task> StartAll()
    {
        List<(string Name, IDatabaseContainer Container)> containers =
            [("SQL Server", SqlServer), ("PostgreSQL", PostgreSql)];
        AddMySql(containers);
        return containers.ToDictionary(c => c.Container, c => StartAsync(c.Name, c.Container));
    }

    private static async Task StartAsync(string name, IDatabaseContainer container)
    {
        try
        {
            await container.StartAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The {name} container ({container.Image.FullName}) did not start.", exception);
        }
    }

    // MySqlProviderTests.cs adds MySQL's container; a framework without a MySQL provider leaves that file out.
    static partial void AddMySql(List<(string Name, IDatabaseContainer Container)> containers);
}

public sealed class SqlServerFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container => DatabaseContainers.SqlServer;

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseSqlServer(ConnectionString);

    public override string WithDatabase(string database) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = database }.ConnectionString;
}

public sealed class PostgreSqlFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container => DatabaseContainers.PostgreSql;

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseNpgsql(ConnectionString);

    public override string WithDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
}

public sealed class ProviderOrder : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;
}

// Table-per-type: TenantId is in the ProviderAnimals table, a dog's Detail in ProviderDogs.
public class ProviderAnimal : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;
}

public sealed class ProviderDog : ProviderAnimal
{
    [MaxLength(64)]
    public string Detail { get; set; } = string.Empty;
}

// A many-to-many relationship with EF Core's own join entity, which carries no TenantId.
public sealed class ProviderPost : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    public List<ProviderTag> Tags { get; } = [];
}

public sealed class ProviderTag : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    public List<ProviderPost> Posts { get; } = [];
}

public sealed class ProviderOrdersContext(DbContextOptions<ProviderOrdersContext> options)
    : DbContext(options)
{
    public DbSet<ProviderOrder> Orders => Set<ProviderOrder>();

    public DbSet<ProviderAnimal> Animals => Set<ProviderAnimal>();

    public DbSet<ProviderPost> Posts => Set<ProviderPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderAnimal>().UseTptMappingStrategy().ToTable("ProviderAnimals");
        modelBuilder.Entity<ProviderDog>().ToTable("ProviderDogs");
        modelBuilder.Entity<ProviderPost>().HasMany(p => p.Tags).WithMany(t => t.Posts);
    }
}
