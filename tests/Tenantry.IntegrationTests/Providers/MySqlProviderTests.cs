using AwesomeAssertions;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
#if NET10_0_OR_GREATER
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;
#else
using MySqlConnector;
#endif
using Testcontainers.MySql;

[assembly: AssemblyFixture(typeof(Tenantry.IntegrationTests.Providers.MySqlFixture))]

namespace Tenantry.IntegrationTests.Providers;

// The MySQL fixture and test classes, kept in one file so a target framework without a MySQL provider
// for its EF Core version (the net11.0 preview lane) can leave them out.

/// <summary>MySQL through Pomelo's provider on EF Core 8 and 9, and Oracle's on EF Core 10, which Pomelo does not support.</summary>
public sealed class MySqlFixture : DatabaseFixture
{
    protected override IDatabaseContainer Container => DatabaseContainers.MySql;

    public override DbContextOptionsBuilder UseProvider(DbContextOptionsBuilder options) =>
#if NET10_0_OR_GREATER
        options.UseMySQL(ConnectionString);
#else
        options.UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 4, 0)));
#endif

    public override string WithDatabase(string database) =>
        new MySqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    public override string Quote(string identifier) => $"`{identifier}`";
}

public sealed class MySqlWriteIsolationTests(MySqlFixture fixture) : ProviderWriteIsolationTests(fixture);

public sealed class MySqlPooledDatabasePerTenantTests(MySqlFixture fixture) : ProviderPooledDatabasePerTenantTests(fixture);

public sealed class MySqlPooledGuardTests(MySqlFixture fixture) : ProviderPooledGuardTests(fixture);

/// <summary>MySQL's default collation ignores case, under Pomelo's provider and Oracle's alike (event 2007).</summary>
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

#if NET10_0_OR_GREATER
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
#endif

    private static DbContextOptionsBuilder<TContext> UseMySql<TContext>(DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext =>
#if NET10_0_OR_GREATER
        options.UseMySQL("Server=unused");
#else
        options.UseMySql("Server=unused", new MySqlServerVersion(new Version(8, 4, 0)));
#endif

    private sealed class UncollatedContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
        modelBuilder.Entity<TenantIdCollationTests.Order>());

    // Each provider's own: Pomelo's UseCollation on an entity type, and Oracle's ForMySQLHasCollation.
    private sealed class TableCollationContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
#if NET10_0_OR_GREATER
        modelBuilder.Entity<TenantIdCollationTests.Order>().ForMySQLHasCollation("utf8mb4_bin"));
#else
        modelBuilder.Entity<TenantIdCollationTests.Order>().UseCollation("utf8mb4_bin"));
#endif

    private sealed class ColumnCollationContext(DbContextOptions options) : TenantIdCollationTests.ModelContext(options, modelBuilder =>
#if NET10_0_OR_GREATER
        modelBuilder.Entity<TenantIdCollationTests.Order>().Property(order => order.TenantId).ForMySQLHasCollation("utf8mb4_bin"));
#else
        modelBuilder.Entity<TenantIdCollationTests.Order>().Property(order => order.TenantId).UseCollation("utf8mb4_bin"));
#endif
}

internal static partial class DatabaseContainers
{
    // Root, because the database-per-tenant tests create a database per tenant.
    public static IDatabaseContainer MySql { get; } =
        new MySqlBuilder(ContainerImages.MySql).WithUsername("root").Build();

    static partial void AddMySql(List<(string Name, IDatabaseContainer Container)> containers) =>
        containers.Add(("MySQL", MySql));
}
