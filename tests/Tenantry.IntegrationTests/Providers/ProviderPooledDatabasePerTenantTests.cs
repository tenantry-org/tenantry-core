using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;

namespace Tenantry.IntegrationTests.Providers;

public sealed class SqlServerPooledDatabasePerTenantTests(SqlServerFixture fixture)
    : ProviderPooledDatabasePerTenantTests(fixture), IClassFixture<SqlServerFixture>;

public sealed class PostgreSqlPooledDatabasePerTenantTests(PostgreSqlFixture fixture)
    : ProviderPooledDatabasePerTenantTests(fixture), IClassFixture<PostgreSqlFixture>;

public sealed class MySqlPooledDatabasePerTenantTests(MySqlFixture fixture)
    : ProviderPooledDatabasePerTenantTests(fixture), IClassFixture<MySqlFixture>;

/// <summary>
/// <c>AddTenantDbContextPool</c> against each real provider: one pooled context instance reused across two
/// tenant databases reads and writes only the current tenant's database. The pool is configured with the
/// fixture's shared connection string, so every lease must override it.
/// </summary>
public abstract class ProviderPooledDatabasePerTenantTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly TenantDescriptor<string> _acme = new() { TenantId = "acme", Name = "Acme" };
    private readonly TenantDescriptor<string> _globex = new() { TenantId = "globex", Name = "Globex" };
    private ServiceProvider _services = null!;

    protected ProviderPooledDatabasePerTenantTests(DatabaseFixture fixture) => _fixture = fixture;

    private ITenantScopeFactory<string> Scopes => _services.GetRequiredService<ITenantScopeFactory<string>>();

    public async Task InitializeAsync()
    {
        _services = Build(poolSize: 4);

        foreach (var tenant in new[] { _acme, _globex })
        {
            await using var scope = Scopes.CreateScope(tenant);
            await scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>().Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var tenant in new[] { _acme, _globex })
        {
            await using var scope = Scopes.CreateScope(tenant);
            await scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>().Database.EnsureDeletedAsync();
        }

        await _services.DisposeAsync();
    }

    [Fact]
    public async Task PooledContext_ReusedAcrossTenantDatabases_ReadsAndWritesOnlyTheCurrentTenants()
    {
        HashSet<Guid> instances = [];
        List<string> seen = [];

        foreach (var tenant in new[] { _acme, _globex, _acme, _globex, _acme })
        {
            await using var scope = Scopes.CreateScope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
            instances.Add(db.ContextId.InstanceId);

            db.Orders.Add(new ProviderOrder { Description = $"{tenant.TenantId} {seen.Count}" });
            await db.SaveChangesAsync();
            seen.Add($"{tenant.TenantId}:{await db.Orders.CountAsync()}");
        }

        instances.Should().ContainSingle("one pooled instance serves every lease");
        seen.Should().Equal("acme:1", "globex:1", "acme:2", "globex:2", "acme:3");
        (await StoredTenantIdsAsync(_acme)).Should().Equal("acme", "acme", "acme");
        (await StoredTenantIdsAsync(_globex)).Should().Equal("globex", "globex");
    }

    [Fact]
    public async Task ConcurrentLeases_EachWriteOnlyToTheirOwnTenantsDatabase()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Scopes.RunInScopeAsync(
            i % 2 == 0 ? "acme" : "globex",
            async (scope, ct) =>
            {
                var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
                db.Orders.Add(new ProviderOrder { Description = $"concurrent {i}" });
                await db.SaveChangesAsync(ct);
            })));

        (await StoredTenantIdsAsync(_acme)).Should().HaveCount(10).And.OnlyContain(id => id == "acme");
        (await StoredTenantIdsAsync(_globex)).Should().HaveCount(10).And.OnlyContain(id => id == "globex");
    }

    // EF Core raises ConnectionOpening only when it opens a closed connection, so a connection opened as Acme
    // and still open when the context is used as Globex must be caught when commands run.
    [Theory]
    [InlineData("OpenConnection")]
    [InlineData("BeginTransaction")]
    public async Task ContextWithAnOpenConnection_UsedAfterTheTenantChanges_RefusesToRunCommands(string openedBy)
    {
        var ambient = _services.GetRequiredService<ITenantScope<string>>();
        ProviderOrdersContext db;

        using (ambient.BeginScope(_acme))
        {
            db = await _services.GetRequiredService<IDbContextFactory<ProviderOrdersContext>>().CreateDbContextAsync();

            if (openedBy == "BeginTransaction")
            {
                await db.Database.BeginTransactionAsync();
            }
            else
            {
                await db.Database.OpenConnectionAsync();
            }
        }

        await using (db)
        using (ambient.BeginScope(_globex))
        {
            db.Orders.Add(new ProviderOrder { Description = "globex order in acme's database" });
            var save = () => db.SaveChangesAsync();
            var read = () => db.Orders.IgnoreQueryFilters().CountAsync();
            var sql = () => db.Database.ExecuteSqlRawAsync("SELECT 1");

            (await save.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*");
            (await read.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*");
            (await sql.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*");
        }

        (await StoredTenantIdsAsync(_acme)).Should().BeEmpty();
        (await StoredTenantIdsAsync(_globex)).Should().BeEmpty();
    }

    private ServiceProvider Build(int poolSize)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant =>
        {
            tenant.UseInMemoryStore([_acme, _globex]);
            tenant.UseConnectionStrings(options =>
                options.GetConnectionString = t => _fixture.WithDatabase($"tk_pool_{t.TenantId}_{_runId}"));
            tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
        });
        services.AddTenantDbContextPool<ProviderOrdersContext, string>(
            (sp, options) => _fixture.UseProvider(options).AddTenantInterceptors(sp),
            poolSize);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    // Every row physically stored in the tenant's database, read through a lease connected to it with the
    // tenant filter removed, so rows written to the wrong database would show up here.
    private Task<List<string>> StoredTenantIdsAsync(TenantDescriptor<string> tenant) =>
        Scopes.RunInScopeAsync(tenant.TenantId, (scope, ct) =>
            scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>().Orders
                .IgnoreQueryFilters()
                .OrderBy(order => order.Id)
                .Select(order => order.TenantId)
                .ToListAsync(ct));
}
