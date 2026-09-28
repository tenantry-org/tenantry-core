using System.ComponentModel.DataAnnotations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tenantry.Core;
using Tenantry.EfCore;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Tenantry.IntegrationTests.Providers;

/// <summary>
/// One database container per provider, shared by every test in a provider's test class. Tests use fresh
/// tenant ids, so they never need a clean database.
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

    public async Task InitializeAsync()
    {
        await Container.StartAsync();

        var options = UseProvider(new DbContextOptionsBuilder<ProviderOrdersContext>());
        await using ProviderOrdersContext db = new((DbContextOptions<ProviderOrdersContext>)options.Options);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

public sealed class SqlServerFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container { get; } =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseSqlServer(ConnectionString);

    public override string WithDatabase(string database) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = database }.ConnectionString;
}

public sealed class PostgreSqlFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container { get; } =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseNpgsql(ConnectionString);

    public override string WithDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;
}

public sealed class ProviderOrder : ITenantScoped<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;
}

public sealed class ProviderOrdersContext(DbContextOptions<ProviderOrdersContext> options)
    : MultiTenantDbContext<string>(options)
{
    public DbSet<ProviderOrder> Orders => Set<ProviderOrder>();
}
