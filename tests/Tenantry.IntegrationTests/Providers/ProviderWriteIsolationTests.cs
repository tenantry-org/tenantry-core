using System.Data.Common;
using System.Transactions;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenantry;
using Tenantry.EfCore;

namespace Tenantry.IntegrationTests.Providers;

public sealed class SqlServerWriteIsolationTests(SqlServerFixture fixture) : ProviderWriteIsolationTests(fixture)
{
    [Fact]
    public async Task InATransactionWithMultipleActiveResultSets_AFailedCheckStopsItsCommit()
    {
        // With MARS, SQL Server has no savepoints, so EF Core cannot undo a failed save inside the transaction: the
        // derived table's UPDATE stays in it, and the application's commit would keep it.
        var id = await AddDogAsync(Acme, "acme detail");
        var options = new DbContextOptionsBuilder<ProviderOrdersContext>()
            .UseSqlServer(new SqlConnectionStringBuilder(fixture.ConnectionString) { MultipleActiveResultSets = true }.ConnectionString)
            .UseApplicationServiceProvider(Services)
            .UseTenantry()
            .Options;

        using (Tenants.Use(Tenant(Globex)))
        {
            await using ProviderOrdersContext db = new(options);
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            ProviderDog stub = new() { Id = id, TenantId = Globex, Detail = "acme detail" };
            db.Animals.Attach(stub);
            stub.Detail = "overwritten";
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
            db.Database.CurrentTransaction.Should().BeNull();
        }

        (await DogDetailAsync(id)).Should().Be("acme detail");
    }
}

public sealed class PostgreSqlWriteIsolationTests(PostgreSqlFixture fixture) : ProviderWriteIsolationTests(fixture)
{
    [Fact]
    public async Task ARefusedTransactionThatEnded_DoesNotRefuseTheNextOne_ThoughNpgsqlHandsOutItsObjectAgain()
    {
        // EF Core fails to roll a forged save back to its savepoint, so that transaction may not commit. It is disposed
        // instead, and Npgsql hands the same transaction object to the next transaction on the connection, which must
        // commit.
        var id = await AddDogAsync(Acme, "acme detail");
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1 }.ConnectionString;
        DbTransaction first;

        using (Tenants.Use(Tenant(Globex)))
        {
            await using ProviderOrdersContext db = new(new DbContextOptionsBuilder<ProviderOrdersContext>()
                .UseNpgsql(connectionString)
                .UseApplicationServiceProvider(Services)
                .AddInterceptors(new SavepointRollbackFails())
                .UseTenantry()
                .Options);
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            first = transaction.GetDbTransaction();
            ProviderDog stub = new() { Id = id, TenantId = Globex, Detail = "acme detail" };
            db.Animals.Attach(stub);
            stub.Detail = "overwritten";
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        await using (NpgsqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var next = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            next.Should().BeSameAs(first, "Npgsql hands out its transaction object again");

            using (Tenants.Use(Tenant(Acme)))
            {
                await using ProviderOrdersContext db = new(new DbContextOptionsBuilder<ProviderOrdersContext>()
                    .UseNpgsql(connection)
                    .UseApplicationServiceProvider(Services)
                    .UseTenantry()
                    .Options);
                await db.Database.UseTransactionAsync(next, TestContext.Current.CancellationToken);
                (await db.Animals.OfType<ProviderDog>().SingleAsync(d => d.Id == id, TestContext.Current.CancellationToken)).Detail = "changed";
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
                await db.Database.CommitTransactionAsync(TestContext.Current.CancellationToken);
            }
        }

        (await DogDetailAsync(id)).Should().Be("changed");
    }

    // A rollback to a save's savepoint that fails, which EF Core only logs.
    private sealed class SavepointRollbackFails : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("rollback failed");
    }
}

/// <summary>
/// Write-isolation guarantees that depend on the database's behaviour, run against each real provider:
/// the stored-tenant predicate on UPDATE/DELETE (and its affected-row count), the default rejection of
/// writes without a tenant, tenant-filtered bulk operations, and pooled contexts.
/// </summary>
public abstract class ProviderWriteIsolationTests : IAsyncDisposable
{
    private readonly DatabaseFixture _fixture;
    private readonly ServiceProvider _services;
    private readonly ITenantContextSetter<string> _tenants;
    private readonly string _acme = $"acme-{Guid.NewGuid():N}";
    private readonly string _globex = $"globex-{Guid.NewGuid():N}";

    protected IServiceProvider Services => _services;

    protected ITenantContextSetter<string> Tenants => _tenants;

    protected string Acme => _acme;

    protected string Globex => _globex;

    protected ProviderWriteIsolationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantry<string>();
        services.AddDbContext<ProviderOrdersContext>(options => fixture.UseProvider(options).UseTenantry());
        _services = services.BuildServiceProvider();
        _tenants = _services.GetRequiredService<ITenantContextSetter<string>>();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _services.DisposeAsync();
    }

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
    public async Task ForgedUpdateOfATablePerTypeEntity_InItsDerivedTableOnly_MatchesNoRow_AndLeavesTheRowUnchanged()
    {
        // EF Core updates only the derived table, so Tenantry writes TenantId back to the root table to be checked.
        var id = await AddDogAsync(_acme, "acme detail");

        var act = () => AsTenantAsync(_globex, db =>
        {
            ProviderDog stub = new() { Id = id, TenantId = _globex, Detail = "acme detail" };
            db.Animals.Attach(stub);
            stub.Detail = "overwritten";
            return db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await DogDetailAsync(id)).Should().Be("acme detail");
    }

    [Fact]
    public async Task WithoutATransaction_AForgedUpdateOfADerivedTable_ChangesNothing()
    {
        // The provider sends both UPDATEs in one batch and runs them all before EF Core reads what each matched, so
        // without a transaction the derived table's would stay: Tenantry has EF Core run the save in one.
        var id = await AddDogAsync(_acme, "acme detail");

        var act = () => AsTenantAsync(_globex, db =>
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            ProviderDog stub = new() { Id = id, TenantId = _globex, Detail = "acme detail" };
            db.Animals.Attach(stub);
            stub.Detail = "overwritten";
            return db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await DogDetailAsync(id)).Should().Be("acme detail");
    }

    [Fact]
    public async Task WithoutATransaction_AForgedDeleteOfATablePerTypeEntity_DeletesNothing()
    {
        var id = await AddDogAsync(_acme, "acme detail");

        var act = () => AsTenantAsync(_globex, db =>
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            db.Animals.Remove(new ProviderDog { Id = id, TenantId = _globex });
            return db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await DogDetailAsync(id)).Should().Be("acme detail");
    }

    [Fact]
    public async Task InATransactionScope_AFailedCheck_RollsTheScopeBack()
    {
        // EF Core sets no savepoint in an ambient transaction, so completing the scope would keep the derived table's
        // UPDATE: Tenantry rolls the transaction back.
        var id = await AddDogAsync(_acme, "acme detail");

        var act = async () =>
        {
            using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);

            await AsTenantAsync(_globex, async db =>
            {
                ProviderDog stub = new() { Id = id, TenantId = _globex, Detail = "acme detail" };
                db.Animals.Attach(stub);
                stub.Detail = "overwritten";
                await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
                return 0;
            });

            scope.Complete();
        };

        await act.Should().ThrowAsync<TransactionAbortedException>();
        (await DogDetailAsync(id)).Should().Be("acme detail");
    }

    [Fact]
    public async Task InATransactionScope_TheTenantsOwnSaveOfADerivedTable_Commits()
    {
        // Tenantry's vote must not turn the connection's single-phase commit into a two-phase one, which PostgreSQL
        // refuses by default.
        var id = await AddDogAsync(_acme, "acme detail");

        using (TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled))
        {
            await AsTenantAsync(_acme, async db =>
            {
                (await db.Animals.OfType<ProviderDog>().SingleAsync(d => d.Id == id)).Detail = "changed";
                return await db.SaveChangesAsync();
            });

            scope.Complete();
        }

        (await DogDetailAsync(id)).Should().Be("changed");
    }

    [Fact]
    public async Task UpdateOfATablePerTypeEntity_InItsDerivedTableOnly_Succeeds()
    {
        // The TenantId written back is unchanged: the provider must count the row it matched.
        var id = await AddDogAsync(_acme, "acme detail");

        await AsTenantAsync(_acme, async db =>
        {
            var dog = await db.Animals.OfType<ProviderDog>().SingleAsync(d => d.Id == id);
            dog.Detail = "changed";
            return await db.SaveChangesAsync();
        });

        (await DogDetailAsync(id)).Should().Be("changed");
    }

    [Fact]
    public async Task DatabaseValues_OfAnotherTenantsRow_AreNotRead()
    {
        var id = await AddOrderAsync(_acme, "acme order");

        var forged = await AsTenantAsync(_globex, db =>
            db.Attach(new ProviderOrder { Id = id, TenantId = _globex }).GetDatabaseValuesAsync());
        var own = await AsTenantAsync(_acme, db =>
            db.Attach(new ProviderOrder { Id = id, TenantId = _acme }).GetDatabaseValuesAsync());

        forged.Should().BeNull();
        own!["Description"].Should().Be("acme order");
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

            using (_tenants.Use(Tenant(_acme)))
            {
                order = await db.Orders.SingleAsync(o => o.Id == id);
            }

            using (_tenants.Use(Tenant(_globex)))
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
        collection.AddTenantry<string>();
        collection.AddPooledDbContextFactory<ProviderOrdersContext>(options => _fixture.UseProvider(options).UseTenantry());
        await using var services = collection.BuildServiceProvider();
        var tenants = services.GetRequiredService<ITenantContextSetter<string>>();
        var factory = services.GetRequiredService<IDbContextFactory<ProviderOrdersContext>>();

        foreach (var tenantId in new[] { _acme, _globex, _acme })
        {
            using (tenants.Use(Tenant(tenantId)))
            {
                await using var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
                db.Orders.Add(new ProviderOrder { Description = "pooled" });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);

                (await db.Orders.Select(o => o.TenantId).Distinct().ToListAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Equal(tenantId);
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

    protected async Task<int> AddDogAsync(string tenantId, string detail) =>
        await AsTenantAsync(tenantId, async db =>
        {
            ProviderDog dog = new() { Detail = detail };
            db.Animals.Add(dog);
            await db.SaveChangesAsync();
            return dog.Id;
        });

    protected async Task<string?> DogDetailAsync(int id)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProviderOrdersContext>();
        return (await db.Animals.IgnoreQueryFilters().AsNoTracking().OfType<ProviderDog>().SingleOrDefaultAsync(d => d.Id == id))?.Detail;
    }

    private async Task<T> AsTenantAsync<T>(string tenantId, Func<ProviderOrdersContext, Task<T>> work)
    {
        using var _ = _tenants.Use(Tenant(tenantId));
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

    protected static TenantDescriptor<string> Tenant(string id) => new() { TenantId = id, Name = id };
}
