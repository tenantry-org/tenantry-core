using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;

namespace Tenantry.IntegrationTests.Providers;

// The MySQL fixture and test classes, kept in one file so a target framework without a MySQL provider
// for its EF Core version (the net11.0 preview lane) can leave them out.

public sealed class MySqlFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container { get; } =
        // Root, because the database-per-tenant tests create a database per tenant.
        new MySqlBuilder("mysql:8.4").WithUsername("root").Build();

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseMySQL(ConnectionString);

    public override string WithDatabase(string database) =>
        new MySqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    public override string Quote(string identifier) => $"`{identifier}`";
}

public sealed class MySqlWriteIsolationTests(MySqlFixture fixture)
    : ProviderWriteIsolationTests(fixture), IClassFixture<MySqlFixture>;

public sealed class MySqlPooledDatabasePerTenantTests(MySqlFixture fixture)
    : ProviderPooledDatabasePerTenantTests(fixture), IClassFixture<MySqlFixture>;

public sealed class MySqlPooledGuardTests(MySqlFixture fixture)
    : ProviderPooledGuardTests(fixture), IClassFixture<MySqlFixture>;
