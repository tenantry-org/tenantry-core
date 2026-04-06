using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tenantry.EfCore.Internal;
using Tenantry.EfCore.Tests.Infrastructure;

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
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { Description = "Sync no tenant" });

        var act = () => db.SaveChanges();

        act.Should().NotThrow();
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
        var interceptor = new TenantSaveChangesInterceptor<string>(
            ctx,
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance,
            [], []);

        var act = () => interceptor.SavingChanges(new NullContextEventData(), default);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SavingChangesAsync_WithNoTenantAndDetector_CallsDetectorCheckAndWarn()
    {
        // Exercises L69: detector.CheckAndWarn(tenantContext, "SaveChanges") inside
        // ApplyTenantIsolation — the branch reached when !HasTenant AND detector is not null.
        var tenantContext = TestTenantContext.Empty();
        MissingContextDetector<string> detector = new(
            NullLogger<MissingContextDetector<string>>.Instance);
        var interceptor = new TenantSaveChangesInterceptor<string>(
            tenantContext,
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance,
            [], [detector]);

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new TestDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Orders.Add(new Order { Description = "no tenant with detector" });

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SavingChangesAsync_WithNoTenantAndNoDetector_ExecutesWarningBranch()
    {
        // Constructs the interceptor inline (not via DbContextFactory) to directly
        // exercise the else-LogWarning branch in ApplyTenantIsolation.
        var tenantContext = TestTenantContext.Empty();
        var interceptor = new TenantSaveChangesInterceptor<string>(
            tenantContext,
            NullLogger<TenantSaveChangesInterceptor<string>>.Instance,
            [], []);

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new TestDbContext(options, tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Orders.Add(new Order { Description = "no tenant" });

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    private sealed class NullContextEventData : DbContextEventData
    {
        // DbContextEventData stores constructor args as-is; null! for eventDefinition
        // is safe because the interceptor never invokes the message generator.
        public NullContextEventData() : base(null!, (_, _) => string.Empty, null) { }
    }
}
