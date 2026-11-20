using AwesomeAssertions;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;
using Testcontainers.MySql;

[assembly: AssemblyFixture(typeof(Tenantry.IntegrationTests.Providers.MySqlFixture))]

namespace Tenantry.IntegrationTests.Providers;

// The MySQL fixture and test classes, kept in one file so a target framework without a MySQL provider
// for its EF Core version (net11.0) can leave them out.

/// <summary>MySQL through Oracle's provider on EF Core 10, which Pomelo's does not support.</summary>
public sealed class MySqlFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container => DatabaseContainers.MySql;

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
        options.UseMySQL(ConnectionString);

    public override string WithDatabase(string database) =>
        new MySqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    public override string Quote(string identifier) => $"`{identifier}`";
}

public sealed class MySqlWriteIsolationTests(MySqlFixture fixture) : ProviderWriteIsolationTests(fixture);

public sealed class MySqlPooledDatabasePerTenantTests(MySqlFixture fixture) : ProviderPooledDatabasePerTenantTests(fixture);

public sealed class MySqlPooledGuardTests(MySqlFixture fixture) : ProviderPooledGuardTests(fixture);

/// <summary>MySQL's default collation ignores case (event 2007).</summary>
public sealed class MySqlTenantIdCollationTests
{
    [Fact]
    public void StringTenantIdsWithoutACollation_AreLoggedAsEvent2007() =>
        TenantIdCollationTests.Warnings<UncollatedContext, string>(UseMySql).Should().ContainSingle()
            .Which.Message.Should().Contain("'UncollatedContext' has string tenant ids in tables whose TenantId column has no collation: Order.");

    [Fact]
    public void ACollationOnTheTable_SilencesIt() =>
        TenantIdCollationTests.Warnings<TableCollationContext, string>(UseMySql).Should().BeEmpty();

    [Fact]
    public void ACollationOnTheColumn_SilencesIt() =>
        TenantIdCollationTests.Warnings<ColumnCollationContext, string>(UseMySql).Should().BeEmpty();

    // Oracle's provider applies only its own ForMySQLHasCollation: EF Core's UseCollation leaves the server's default.
    [Fact]
    public void UnderOraclesProvider_UseCollationOnTheColumnOrTheModel_IsLoggedAsEvent2007()
    {
        TenantIdCollationTests.Warnings<RelationalColumnCollationContext, string>(UseMySql).Should().ContainSingle();
        TenantIdCollationTests.Warnings<RelationalModelCollationContext, string>(UseMySql).Should().ContainSingle();
    }

    private sealed class RelationalColumnCollationContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
        modelBuilder.Entity<TenantIdCollationTests.Order>().Property(order => order.TenantId).UseCollation("utf8mb4_bin"));

    private sealed class RelationalModelCollationContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
    {
        RelationalModelBuilderExtensions.UseCollation(modelBuilder, "utf8mb4_bin");
        modelBuilder.Entity<TenantIdCollationTests.Order>();
    });

    private static DbContextOptionsBuilder<TContext> UseMySql<TContext>(DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext =>
        options.UseMySQL("Server=unused");

    private sealed class UncollatedContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
        modelBuilder.Entity<TenantIdCollationTests.Order>());

    // Oracle's provider's own ForMySQLHasCollation.
    private sealed class TableCollationContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
        modelBuilder.Entity<TenantIdCollationTests.Order>().ForMySQLHasCollation("utf8mb4_bin"));

    private sealed class ColumnCollationContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
        modelBuilder.Entity<TenantIdCollationTests.Order>().Property(order => order.TenantId).ForMySQLHasCollation("utf8mb4_bin"));
}

internal static partial class DatabaseContainers
{
    // Root, because the database-per-tenant tests create a database per tenant.
    public static IDatabaseContainer MySql { get; } =
        new MySqlBuilder(ContainerImages.MySql).WithUsername("root").Build();

    static partial void AddMySql(List<(string Name, IDatabaseContainer Container)> containers) =>
        containers.Add(("MySQL", MySql));
}
