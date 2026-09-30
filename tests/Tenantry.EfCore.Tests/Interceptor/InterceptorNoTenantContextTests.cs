using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tenantry;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Verifies SaveChanges without a resolved tenant: tenant-scoped writes are rejected by default, maintenance
/// writes are possible only through an explicit <see cref="EfCoreIsolationOptions.OnMissingTenant"/> opt-in,
/// and saves that write no tenant-scoped entity are unaffected.
/// </summary>
public sealed class InterceptorNoTenantContextTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Default_AddingScopedEntityWithoutTenant_ThrowsAndPersistsNothing()
    {
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.AsNone(), _connection);
        db.Orders.Add(new Order { Description = "no tenant" });

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantNotResolvedException>().WithMessage("*Order*");
        CountRows().Should().Be(0);
    }

    [Fact]
    public async Task Default_SyncSaveWithoutTenant_Throws()
    {
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.AsNone(), _connection);
        db.Orders.Add(new Order { Description = "no tenant" });

        db.Invoking(d => d.SaveChanges())
            .Should().Throw<TenantNotResolvedException>();
        CountRows().Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_UpdatingOrRemovingExistingRowWithoutTenant_ThrowsAndLeavesRowUnchanged(bool remove)
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.AsNone(), _connection);
        Order forged = new() { Id = acmeOrderId, TenantId = "globex", Description = "overwritten" };

        if (remove)
        {
            db.Orders.Remove(forged);
        }
        else
        {
            db.Orders.Update(forged);
        }

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantNotResolvedException>();
        ReadRow(acmeOrderId).Should().Be(("acme", "acme order"));
    }

    [Fact]
    public async Task Default_WritingOnlyNonTenantEntitiesWithoutTenant_Succeeds()
    {
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.AsNone(), _connection);
        db.NonTenants.Add(new NonTenant { Name = "catalogue entry" });

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(MissingTenantBehavior.Allow)]
    [InlineData(MissingTenantBehavior.Warn)]
    public async Task OptIn_AddingScopedEntityWithExplicitTenantId_Succeeds(MissingTenantBehavior behavior)
    {
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(
            _tenant.AsNone(), _connection, new EfCoreIsolationOptions { OnMissingTenant = behavior });
        db.Orders.Add(new Order { TenantId = "acme", Description = "seeded" });

        await db.SaveChangesAsync();

        CountRows().Should().Be(1);
    }

    [Theory]
    [InlineData(MissingTenantBehavior.Allow)]
    [InlineData(MissingTenantBehavior.Warn)]
    public async Task OptIn_AddingScopedEntityWithoutTenantId_ThrowsAndPersistsNothing(MissingTenantBehavior behavior)
    {
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(
            _tenant.AsNone(), _connection, new EfCoreIsolationOptions { OnMissingTenant = behavior });
        db.Orders.Add(new Order { Description = "unowned" });

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantNotResolvedException>().WithMessage("*TenantId*");
        CountRows().Should().Be(0);
    }

    [Theory]
    [InlineData(MissingTenantBehavior.Allow)]
    [InlineData(MissingTenantBehavior.Warn)]
    public async Task OptIn_UpdatingExistingRowWithoutTenant_Succeeds(MissingTenantBehavior behavior)
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(
            _tenant.AsNone(), _connection, new EfCoreIsolationOptions { OnMissingTenant = behavior });

        db.Orders.Update(new Order { Id = acmeOrderId, TenantId = "acme", Description = "maintenance" });
        await db.SaveChangesAsync();

        ReadRow(acmeOrderId).Should().Be(("acme", "maintenance"));
    }

    [Fact]
    public void Apply_WhenNoTenantContext_ReturnsWithoutProcessingEntries()
    {
        // TenantWriteIsolationApplier.Apply has an early-return guard for !HasTenant.
        // The interceptor short-circuits before calling Apply in this case, so we test
        // Apply directly to cover that defensive branch.
        var act = () => TenantWriteIsolationApplier.Apply([], _tenant.AsNone());

        act.Should().NotThrow();
    }

    [Fact]
    public void SavingChanges_WithNullContext_DoesNotThrow()
    {
        // DbContextEventData.Context is DbContext? — null is a valid (if rare) input.
        // Exercises the null-context guard in ApplyTenantIsolation.
        TenantSaveChangesInterceptor<string> interceptor = new(
            _tenant.As("acme"),
            new EfCoreIsolationOptions(),
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance);

        var act = () => interceptor.SavingChanges(new NullContextEventData(), default);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Apply_SkipsNonTenantScopedEntities()
    {
        // Verifies the branch where an entry.Entity is not ITenantEntity — the applier should skip it.
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("acme"), _connection);
        db.NonTenants.Add(new NonTenant { Name = "plain" });

        db.Invoking(d => TenantWriteIsolationApplier.Apply(d.ChangeTracker.Entries(), _tenant)).Should().NotThrow();
    }

    private async Task<int> SeedAcmeOrderAsync()
    {
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("acme"), _connection);
        Order order = new() { Description = "acme order" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private long CountRows()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Orders";
        return (long)command.ExecuteScalar()!;
    }

    private (string TenantId, string Description)? ReadRow(int id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT TenantId, Description FROM Orders WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    private sealed class NullContextEventData : DbContextEventData
    {
        // DbContextEventData stores constructor args as-is; null! for eventDefinition
        // it is safe because the interceptor never invokes the message generator.
        public NullContextEventData() : base(null!, (_, _) => string.Empty, null) { }
    }
}
