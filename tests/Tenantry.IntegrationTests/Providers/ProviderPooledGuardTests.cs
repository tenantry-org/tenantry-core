using System.ComponentModel.DataAnnotations;
using System.Transactions;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore;
using Tenantry.EfCore.Extensions;

namespace Tenantry.IntegrationTests.Providers;

public sealed class HiLoItem : ITenantScoped<string>
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Text { get; set; } = string.Empty;
}

public sealed class PostgreSqlHiLoContext(DbContextOptions<PostgreSqlHiLoContext> options)
    : MultiTenantDbContext<string>(options)
{
    public DbSet<HiLoItem> Items => Set<HiLoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        NpgsqlPropertyBuilderExtensions.UseHiLo(modelBuilder.Entity<HiLoItem>().Property(i => i.Id), "item_hilo");
    }
}

public sealed class SqlServerHiLoContext(DbContextOptions<SqlServerHiLoContext> options)
    : MultiTenantDbContext<string>(options)
{
    public DbSet<HiLoItem> Items => Set<HiLoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        SqlServerPropertyBuilderExtensions.UseHiLo(modelBuilder.Entity<HiLoItem>().Property(i => i.Id), "item_hilo");
    }
}

public sealed class PostgreSqlPooledHiLoTests(PostgreSqlFixture fixture)
    : ProviderPooledHiLoTests<PostgreSqlHiLoContext>(fixture), IClassFixture<PostgreSqlFixture>
{
    protected override async Task<string> ReadSequenceAsync(string database)
    {
        await using NpgsqlConnection connection = new(Fixture.WithDatabase(database));
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT last_value || '/' || is_called FROM item_hilo", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

public sealed class SqlServerPooledHiLoTests(SqlServerFixture fixture)
    : ProviderPooledHiLoTests<SqlServerHiLoContext>(fixture), IClassFixture<SqlServerFixture>
{
    protected override async Task<string> ReadSequenceAsync(string database)
    {
        await using SqlConnection connection = new(Fixture.WithDatabase(database));
        await connection.OpenAsync();
        await using SqlCommand command = new(
            "SELECT CAST(current_value AS nvarchar(40)) + '/' + ISNULL(CAST(last_used_value AS nvarchar(40)), 'unused') " +
            "FROM sys.sequences WHERE name = 'item_hilo'",
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

/// <summary>
/// EF Core fetches HiLo key blocks with a command that carries no context, so the guard attributes it to the
/// context that owns the connection. Without that, a context opened as Acme and used as Globex would draw
/// Globex's keys from Acme's sequence.
/// </summary>
public abstract class ProviderPooledHiLoTests<TContext> : IAsyncLifetime
    where TContext : DbContext
{
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly TenantDescriptor<string> _acme = new() { TenantId = "acme", Name = "Acme" };
    private readonly TenantDescriptor<string> _globex = new() { TenantId = "globex", Name = "Globex" };
    private ServiceProvider _services = null!;

    protected ProviderPooledHiLoTests(DatabaseFixture fixture) => Fixture = fixture;

    protected DatabaseFixture Fixture { get; }

    private ITenantScope<string> Ambient => _services.GetRequiredService<ITenantScope<string>>();

    private IDbContextFactory<TContext> Factory => _services.GetRequiredService<IDbContextFactory<TContext>>();

    protected abstract Task<string> ReadSequenceAsync(string database);

    public async Task InitializeAsync()
    {
        _services = BuildPool<TContext>(Fixture, _runId, "hilo", _acme, _globex);

        foreach (var tenant in new[] { _acme, _globex })
        {
            using (Ambient.BeginScope(tenant))
            {
                await using var db = await Factory.CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var tenant in new[] { _acme, _globex })
        {
            using (Ambient.BeginScope(tenant))
            {
                await using var db = await Factory.CreateDbContextAsync();
                await db.Database.EnsureDeletedAsync();
            }
        }

        await _services.DisposeAsync();
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("None", false)]
    [InlineData("OpenConnection", true)]
    [InlineData("OpenConnection", false)]
    [InlineData("BeginTransaction", true)]
    public async Task HiLoKey_AddedAsGlobex_OnAcmesContext_Throws(string openedBy, bool async)
    {
        var acmeBefore = await ReadSequenceAsync(Database(_acme));
        TContext db;

        using (Ambient.BeginScope(_acme))
        {
            db = await Factory.CreateDbContextAsync();

            if (openedBy == "OpenConnection")
            {
                await db.Database.OpenConnectionAsync();
            }
            else if (openedBy == "BeginTransaction")
            {
                await db.Database.BeginTransactionAsync();
            }
        }

        HiLoItem item = new() { Text = "globex item" };

        await using (db)
        using (Ambient.BeginScope(_globex))
        {
            Func<Task> add = async ? async () => await db.AddAsync(item) : () => Task.FromResult(db.Add(item));

            (await add.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*");
        }

        item.Id.Should().Be(0);
        (await ReadSequenceAsync(Database(_acme))).Should().Be(acmeBefore, "Globex must not advance Acme's sequence");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiLoKey_AddedByTheOwningTenant_Works(bool openConnection)
    {
        using (Ambient.BeginScope(_acme))
        {
            await using var db = await Factory.CreateDbContextAsync();

            if (openConnection)
            {
                await db.Database.OpenConnectionAsync();
            }

            HiLoItem item = new() { Text = "acme item" };
            await db.AddAsync(item);
            await db.SaveChangesAsync();

            item.Id.Should().BePositive();
        }
    }

    private string Database(TenantDescriptor<string> tenant) => $"tk_hilo_{tenant.TenantId}_{_runId}";

    internal static ServiceProvider BuildPool<TPooled>(
        DatabaseFixture fixture,
        string runId,
        string prefix,
        params TenantDescriptor<string>[] tenants)
        where TPooled : DbContext
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant =>
        {
            tenant.UseInMemoryStore(tenants);
            tenant.UseConnectionStrings(options =>
                options.GetConnectionString = t => fixture.WithDatabase($"tk_{prefix}_{t.TenantId}_{runId}"));
            tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
        });
        services.AddTenantDbContextPool<TPooled, string>(
            (sp, options) => fixture.UseProvider(options).AddTenantInterceptors(sp),
            poolSize: 4);

        return services.BuildServiceProvider();
    }
}

public sealed class SqlServerPooledGuardTests(SqlServerFixture fixture)
    : ProviderPooledGuardTests(fixture), IClassFixture<SqlServerFixture>;

public sealed class PostgreSqlPooledGuardTests(PostgreSqlFixture fixture)
    : ProviderPooledGuardTests(fixture), IClassFixture<PostgreSqlFixture>;

public sealed class MySqlPooledGuardTests(MySqlFixture fixture)
    : ProviderPooledGuardTests(fixture), IClassFixture<MySqlFixture>;

/// <summary>
/// Database creation, migrations, raw SQL and transactions on a context whose transaction Acme opened, used
/// while Globex is current. Whatever each operation does, Acme's data must be untouched.
/// </summary>
public abstract class ProviderPooledGuardTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly TenantDescriptor<string> _acme = new() { TenantId = "acme", Name = "Acme" };
    private readonly TenantDescriptor<string> _globex = new() { TenantId = "globex", Name = "Globex" };
    private ServiceProvider _services = null!;

    private ITenantScope<string> Ambient => _services.GetRequiredService<ITenantScope<string>>();

    private IDbContextFactory<ProviderOrdersContext> Factory =>
        _services.GetRequiredService<IDbContextFactory<ProviderOrdersContext>>();

    public async Task InitializeAsync()
    {
        _services = ProviderPooledHiLoTests<ProviderOrdersContext>.BuildPool<ProviderOrdersContext>(
            fixture, _runId, "guard", _acme, _globex);

        foreach (var tenant in new[] { _acme, _globex })
        {
            using (Ambient.BeginScope(tenant))
            {
                await using var db = await Factory.CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
                db.Orders.Add(new ProviderOrder { Description = $"{tenant.TenantId} seed" });
                await db.SaveChangesAsync();
            }
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var tenant in new[] { _acme, _globex })
        {
            using (Ambient.BeginScope(tenant))
            {
                await using var db = await Factory.CreateDbContextAsync();
                await db.Database.EnsureDeletedAsync();
            }
        }

        await _services.DisposeAsync();
    }

    // CanConnect swallows the guard's exception; creating a savepoint or committing only finishes Acme's own
    // work. The rest must throw.
    [Theory]
    [InlineData("EnsureDeleted", true)]
    [InlineData("EnsureDeletedAsync", true)]
    [InlineData("EnsureCreated", true)]
    [InlineData("Migrate", true)]
    [InlineData("GetAppliedMigrations", true)]
    [InlineData("SqlQueryRaw", true)]
    [InlineData("ExecuteUpdate", true)]
    [InlineData("CanConnect", false)]
    [InlineData("CreateSavepoint", false)]
    [InlineData("CommitAcmeTransaction", false)]
    public async Task OperationAsGlobex_OnAcmesOpenTransaction_LeavesAcmesDataIntact(string operation, bool throws)
    {
        ProviderOrdersContext db;

        using (Ambient.BeginScope(_acme))
        {
            db = await Factory.CreateDbContextAsync();
            await db.Database.BeginTransactionAsync();
        }

        Exception? error = null;

        await using (db)
        using (Ambient.BeginScope(_globex))
        {
            try
            {
                await (operation switch
                {
                    "EnsureDeleted" => Task.FromResult(db.Database.EnsureDeleted()),
                    "EnsureDeletedAsync" => db.Database.EnsureDeletedAsync(),
                    "EnsureCreated" => db.Database.EnsureCreatedAsync(),
                    "Migrate" => db.Database.MigrateAsync(),
                    "GetAppliedMigrations" => db.Database.GetAppliedMigrationsAsync(),
                    "SqlQueryRaw" => db.Database.SqlQueryRaw<string>(SelectDescriptions()).ToListAsync(),
                    "ExecuteUpdate" => db.Orders.IgnoreQueryFilters()
                        .ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "overwritten by globex")),
                    "CanConnect" => db.Database.CanConnectAsync(),
                    "CreateSavepoint" => db.Database.CurrentTransaction!.CreateSavepointAsync("globex"),
                    "CommitAcmeTransaction" => db.Database.CommitTransactionAsync(),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                });
            }
            catch (Exception e)
            {
                error = e;
            }
        }

        if (throws)
        {
            error.Should().BeOfType<TenantIsolationViolationException>();
        }

        (await DescriptionsAsync(_acme)).Should().Equal("acme seed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbientTransactionScope_AcmesContextUsedAsGlobex_Throws(bool scopeStartedByAcme)
    {
        ProviderOrdersContext db;
        TransactionScope? scope = null;

        using (Ambient.BeginScope(_acme))
        {
            if (scopeStartedByAcme)
            {
                scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            }

            db = await Factory.CreateDbContextAsync();
            await db.Database.OpenConnectionAsync();
        }

        await using (db)
        using (Ambient.BeginScope(_globex))
        {
            scope ??= new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            try
            {
                var update = () => db.Orders.IgnoreQueryFilters()
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "overwritten by globex"));

                await update.Should().ThrowAsync<TenantIsolationViolationException>();
            }
            finally
            {
                scope.Dispose();
            }
        }

        (await DescriptionsAsync(_acme)).Should().Equal("acme seed");
    }

    private string SelectDescriptions()
    {
        string Quote(string name) => fixture is MySqlFixture ? $"`{name}`" : $"\"{name}\"";

        return $"SELECT {Quote("Description")} AS {Quote("Value")} FROM {Quote("Orders")}";
    }

    private async Task<List<string>> DescriptionsAsync(TenantDescriptor<string> tenant)
    {
        using (Ambient.BeginScope(tenant))
        {
            await using var db = await Factory.CreateDbContextAsync();
            return await db.Orders.IgnoreQueryFilters().Select(o => o.Description).ToListAsync();
        }
    }
}

/// <summary>
/// Npgsql can supply connections from an <see cref="NpgsqlDataSource"/>, passed to <c>UseNpgsql</c> or registered
/// in DI. Every lease must still be connected to its tenant's own database.
/// </summary>
public sealed class NpgsqlDataSourcePooledTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly TenantDescriptor<string> _acme = new() { TenantId = "acme", Name = "Acme" };
    private readonly TenantDescriptor<string> _globex = new() { TenantId = "globex", Name = "Globex" };

    [Theory]
    [InlineData("DataSourceInDi")]
    [InlineData("DataSourcePassed")]
    public async Task DataSource_DoesNotOverrideTheTenantsConnectionString(string mode)
    {
        await using var shared = NpgsqlDataSource.Create(fixture.ConnectionString);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant =>
        {
            tenant.UseInMemoryStore([_acme, _globex]);
            tenant.UseConnectionStrings(options =>
                options.GetConnectionString = t => fixture.WithDatabase($"tk_ds_{t.TenantId}_{_runId}"));
            tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
        });

        if (mode == "DataSourceInDi")
        {
            services.AddSingleton(shared);
        }

        services.AddTenantDbContextPool<ProviderOrdersContext, string>(
            (sp, options) => (mode == "DataSourceInDi" ? options.UseNpgsql() : options.UseNpgsql(shared))
                .AddTenantInterceptors(sp),
            poolSize: 4);
        await using var provider = services.BuildServiceProvider();
        var ambient = provider.GetRequiredService<ITenantScope<string>>();
        var factory = provider.GetRequiredService<IDbContextFactory<ProviderOrdersContext>>();
        List<string> databases = [];

        try
        {
            foreach (var tenant in new[] { _acme, _globex })
            {
                using (ambient.BeginScope(tenant))
                {
                    await using var db = await factory.CreateDbContextAsync();
                    databases.Add($"{tenant.TenantId} -> {db.Database.GetDbConnection().Database}");
                    await db.Database.EnsureCreatedAsync();
                    db.Orders.Add(new ProviderOrder { Description = $"{tenant.TenantId} via {mode}" });
                    await db.SaveChangesAsync();
                }
            }
        }
        finally
        {
            foreach (var tenant in new[] { _acme, _globex })
            {
                using (ambient.BeginScope(tenant))
                {
                    await using var db = await factory.CreateDbContextAsync();
                    await db.Database.EnsureDeletedAsync();
                }
            }
        }

        databases.Should().Equal($"acme -> tk_ds_acme_{_runId}", $"globex -> tk_ds_globex_{_runId}");
    }
}
