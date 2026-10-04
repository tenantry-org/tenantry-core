using System.ComponentModel.DataAnnotations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tenantry;
using Tenantry.EfCore;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

// Every test in this assembly needs Docker: CONTRIBUTING.md shows how to run the others (`--filter "Category!=Integration"`).
[assembly: Trait("Category", "Integration")]

// One container per database for each framework's test run, shared by every test class (MySQL's is in
// MySqlProviderTests.cs).
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
        await Container.StartAsync();

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

public sealed class SqlServerFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container { get; } =
        new MsSqlBuilder(ContainerImages.SqlServer).Build();

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseSqlServer(ConnectionString);

    public override string WithDatabase(string database) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = database }.ConnectionString;
}

public sealed class PostgreSqlFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container { get; } =
        new PostgreSqlBuilder(ContainerImages.PostgreSql).Build();

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
