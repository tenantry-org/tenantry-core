using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;

namespace Tenantry.IntegrationTests.Providers;

public sealed class SqlServerWriteIsolationTests(SqlServerFixture fixture)
    : ProviderWriteIsolationTests(fixture), IClassFixture<SqlServerFixture>;

public sealed class PostgreSqlWriteIsolationTests(PostgreSqlFixture fixture)
    : ProviderWriteIsolationTests(fixture), IClassFixture<PostgreSqlFixture>;

public sealed class MySqlWriteIsolationTests(MySqlFixture fixture)
    : ProviderWriteIsolationTests(fixture), IClassFixture<MySqlFixture>;

/// <summary>
/// Write-isolation guarantees that depend on the database's behaviour, run against each real provider:
/// the stored-tenant predicate on UPDATE/DELETE (and its affected-row count), the default rejection of
/// writes without a tenant, tenant-filtered bulk operations, and pooled contexts.
/// </summary>
public abstract class ProviderWriteIsolationTests : IAsyncDisposable
{
    private readonly DatabaseFixture _fixture;
    private readonly ServiceProvider _services;
    private readonly ITenantScope<string> _tenants;
    private readonly string _acme = $"acme-{Guid.NewGuid():N}";
    private readonly string _globex = $"globex-{Guid.NewGuid():N}";

    protected ProviderWriteIsolationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant => tenant.AddEfCoreIsolation());
        services.AddDbContext<ProviderOrdersContext>(options => fixture.UseProvider(options));
        _services = services.BuildServiceProvider();
        _tenants = _services.GetRequiredService<ITenantScope<string>>();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task ForgedDetachedUpdate_MatchesNoRow_AndLeavesTheRowUnchanged()
    {
        var id = await AddOrderAsync(_acme, "acme order");

        var act = () => AsTenantAsync(_globex, db =>
        {
            db.Orders.Update(new ProviderOrder { Id = id, TenantId = _globex, Description = "overwritten" });
            return db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await ReadAsync(id)).Should().Be((_acme, "acme order"));
    }

    [Fact]
    public async Task ForgedDetachedRemove_MatchesNoRow_AndKeepsTheRow()
    {
        var id = await AddOrderAsync(_acme, "acme order");

        var act = () => AsTenantAsync(_globex, db =>
        {
            db.Orders.Remove(new ProviderOrder { Id = id, TenantId = _globex });
            return db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await ReadAsync(id)).Should().Be((_acme, "acme order"));
    }

    [Fact]
    public async Task EntityLoadedUnderAnotherTenant_ThrowsIsolationViolation()
    {
        var id = await AddOrderAsync(_acme, "acme order");

        var act = async () =>
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
            ProviderOrder order;

            using (_tenants.BeginScope(Tenant(_acme)))
            {
                order = await db.Orders.SingleAsync(o => o.Id == id);
            }

            using (_tenants.BeginScope(Tenant(_globex)))
            {
                order.Description = "moved";
                order.TenantId = _globex;
                await db.SaveChangesAsync();
            }
        };

        await act.Should().ThrowAsync<TenantIsolationViolationException>();
        (await ReadAsync(id)).Should().Be((_acme, "acme order"));
    }

    [Theory]
    [InlineData("updated")]
    [InlineData("acme order")] // no value changes: some providers count changed rather than matched rows
    public async Task DetachedUpdateOfOwnRow_Succeeds(string description)
    {
        var id = await AddOrderAsync(_acme, "acme order");

        await AsTenantAsync(_acme, db =>
        {
            db.Orders.Update(new ProviderOrder { Id = id, TenantId = _acme, Description = description });
            return db.SaveChangesAsync();
        });

        (await ReadAsync(id)).Should().Be((_acme, description));
    }

    [Fact]
    public async Task InsertWithoutTenant_IsRejectedByDefault()
    {
        var marker = $"no tenant {Guid.NewGuid():N}"[..40];

        var act = async () =>
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
            db.Orders.Add(new ProviderOrder { Description = marker });
            await db.SaveChangesAsync();
        };

        await act.Should().ThrowAsync<TenantNotResolvedException>();
        (await CountAsync(o => o.Description == marker)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteUpdateAndDelete_OnlyAffectTheCurrentTenant()
    {
        var acmeId = await AddOrderAsync(_acme, "acme order");
        var globexId = await AddOrderAsync(_globex, "globex order");

        var updated = await AsTenantAsync(_globex, db =>
            db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "bulk")));

        updated.Should().Be(1);
        (await ReadAsync(acmeId)).Should().Be((_acme, "acme order"));
        (await ReadAsync(globexId)).Should().Be((_globex, "bulk"));

        var deleted = await AsTenantAsync(_globex, db => db.Orders.ExecuteDeleteAsync());

        deleted.Should().Be(1);
        (await ReadAsync(acmeId)).Should().NotBeNull();
        (await ReadAsync(globexId)).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteUpdateSettingTenantId_IsRejected()
    {
        var id = await AddOrderAsync(_acme, "acme order");

        var act = () => AsTenantAsync(_acme, db =>
            db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.TenantId, _globex)));

        await act.Should().ThrowAsync<TenantIsolationViolationException>();
        (await ReadAsync(id)).Should().Be((_acme, "acme order"));
    }

    [Fact]
    public async Task PooledContexts_IsolateEachTenantTheyServe()
    {
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddTenantryCore<string>(tenant => tenant.AddEfCoreIsolation());
        collection.AddPooledDbContextFactory<ProviderOrdersContext>((sp, options) =>
            _fixture.UseProvider(options).AddTenantInterceptors(sp));
        await using var services = collection.BuildServiceProvider();
        var tenants = services.GetRequiredService<ITenantScope<string>>();
        var factory = services.GetRequiredService<IDbContextFactory<ProviderOrdersContext>>();

        foreach (var tenantId in new[] { _acme, _globex, _acme })
        {
            using (tenants.BeginScope(Tenant(tenantId)))
            {
                await using var db = await factory.CreateDbContextAsync();
                db.Orders.Add(new ProviderOrder { Description = "pooled" });
                await db.SaveChangesAsync();

                (await db.Orders.Select(o => o.TenantId).Distinct().ToListAsync()).Should().Equal(tenantId);
            }
        }
    }

    private async Task<int> AddOrderAsync(string tenantId, string description) =>
        await AsTenantAsync(tenantId, async db =>
        {
            ProviderOrder order = new() { Description = description };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.Id;
        });

    private async Task<T> AsTenantAsync<T>(string tenantId, Func<ProviderOrdersContext, Task<T>> work)
    {
        using var _ = _tenants.BeginScope(Tenant(tenantId));
        await using var scope = _services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>());
    }

    // Reads bypass the tenant filter on purpose: they check what is actually stored.
    private async Task<(string TenantId, string Description)?> ReadAsync(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
        var row = await db.Orders.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id);
        return row is null ? null : (row.TenantId, row.Description);
    }

    private async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<ProviderOrder, bool>> predicate)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
        return await db.Orders.IgnoreQueryFilters().CountAsync(predicate);
    }

    private static TenantDescriptor<string> Tenant(string id) => new() { TenantId = id, Name = id };
}
