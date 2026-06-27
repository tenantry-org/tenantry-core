using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Verifies that SaveChanges without a resolved tenant context logs a warning and
/// does not throw — entities are saved but without TenantId stamped.
/// </summary>
public sealed class InterceptorNoTenantContextTests
{
    [Fact]
    public async Task SaveChanges_WithNoTenantContext_DoesNotThrow()
    {
        var ctx = TestTenantContext.Empty();
        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "No tenant" });

        Func<Task> act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveChanges_WithNoTenantContext_SavesEntity()
    {
        var ctx = TestTenantContext.Empty();
        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "No tenant order" });
        await db.SaveChangesAsync();

        // Query with IgnoreQueryFilters since there's no active tenant filter
        var all = await db.Orders.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        all.Should().HaveCount(1);
        all[0].Description.Should().Be("No tenant order");
        all[0].TenantId.Should().BeEmpty(); // not stamped
    }

    [Fact]
    public async Task SaveChanges_Sync_WithNoTenantContext_DoesNotThrow()
    {
        // Exercises the synchronous SavingChanges interceptor overload, which is not
        // called by SaveChangesAsync. Uses no-tenant context to also cover the
        // no-detector warning branch in ApplyTenantIsolation.
        var ctx = TestTenantContext.Empty();
        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "Sync no tenant" });

        var act = () => db.SaveChanges();

        act.Should().NotThrow();
        
        await db.DisposeAsync();
    }

    [Fact]
    public void Apply_WhenNoTenantContext_ReturnsWithoutProcessingEntries()
    {
        // TenantWriteIsolationApplier.Apply has an early-return guard for !HasTenant.
        // The interceptor short-circuits before calling Apply in this case, so we test
        // Apply directly to cover that defensive branch.
        var ctx = TestTenantContext.Empty();

        var act = () => TenantWriteIsolationApplier.Apply([], ctx);

        act.Should().NotThrow();
    }

    [Fact]
    public void SavingChanges_WithNullContext_DoesNotThrow()
    {
        // DbContextEventData.Context is DbContext? — null is a valid (if rare) input.
        // Exercises the null-context guard in ApplyTenantIsolation.
        var ctx = TestTenantContext.For("acme");
        var interceptor = BuildInterceptor(ctx);

        var act = () => interceptor.SavingChanges(new NullContextEventData(), default);

        act.Should().NotThrow();
    }

    private static TenantSaveChangesInterceptor<string> BuildInterceptor(
        TestTenantContext ctx,
        EfCoreIsolationOptions? options = null) =>
        new(ctx,
            options ?? new EfCoreIsolationOptions(),
            new StrictIsolationValidator<string>(NullLogger<StrictIsolationValidator<string>>.Instance),
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance);

    [Fact]
    public async Task SaveChangesAsync_WithNoTenant_AndRejectPolicy_Throws()
    {
        // OnMissingTenant = Reject must throw before persisting when no tenant is resolved.
        var tenantContext = TestTenantContext.Empty();
        var interceptor = BuildInterceptor(
            tenantContext,
            new EfCoreIsolationOptions { OnMissingTenant = MissingTenantBehavior.Reject });

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var db = new TestDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Orders.Add(new Order { Description = "no tenant reject" });

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantNotResolvedException>();

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithNoTenant_AndAllowPolicy_DoesNotThrow()
    {
        // OnMissingTenant = Allow proceeds silently without a stamped tenant.
        var tenantContext = TestTenantContext.Empty();
        var interceptor = BuildInterceptor(
            tenantContext,
            new EfCoreIsolationOptions { OnMissingTenant = MissingTenantBehavior.Allow });

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var db = new TestDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Orders.Add(new Order { Description = "no tenant allow" });

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await db.DisposeAsync();
    }

    [Fact]
    public async Task SavingChangesAsync_WithNoTenant_AndWarnPolicy_DoesNotThrow()
    {
        // The default Warn policy logs but proceeds.
        var tenantContext = TestTenantContext.Empty();
        var interceptor = BuildInterceptor(tenantContext);

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var db = new TestDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Orders.Add(new Order { Description = "no tenant" });

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await db.DisposeAsync();
    }

    private sealed class NullContextEventData : DbContextEventData
    {
        // DbContextEventData stores constructor args as-is; null! for eventDefinition
        // it is safe because the interceptor never invokes the message generator.
        public NullContextEventData() : base(null!, (_, _) => string.Empty, null) { }
    }

    [Fact]
    public async Task Apply_SkipsNonTenantScopedEntities()
    {
        // Verifies the branch where an entry.Entity is not ITenantScoped — the applier should skip it.
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        // A mapped entity type that does not implement ITenantScoped<string>
        var nonTenant = new NonTenant { Name = "plain" };

        // Add to change tracker (no need to save) so there's an EntityEntry for it
        db.NonTenants.Add(nonTenant);

        var act = () => TenantWriteIsolationApplier.Apply(db.ChangeTracker.Entries(), ctx);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }
}
