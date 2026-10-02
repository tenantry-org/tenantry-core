using AwesomeAssertions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// The checks the save interceptor applies to the change tracker's entries while a tenant is current. A new entity
/// that names another tenant is always rejected.
/// </summary>
public sealed class TenantWriteGuardTests
{
    [Fact]
    public async Task AddedEntity_DefaultTenantId_IsStampedWithTheCurrentTenant()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);
        // TenantId = null (the actual default for string) — it is stamped
        Order order = new() { TenantId = null!, Description = "unstamped" };
        db.Orders.Add(order);

        TenantWriteGuard<string>.Check(db);

        order.TenantId.Should().Be("acme");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_EmptyStringTenantId_IsStampedWithTheCurrentTenant()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);
        // string.Empty is treated as unstamped (devs often init strings to "" rather than
        // null), so it is stamped just like null.
        Order order = new() { TenantId = string.Empty, Description = "unstamped" };
        db.Orders.Add(order);

        TenantWriteGuard<string>.Check(db);

        order.TenantId.Should().Be("acme");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_MatchingTenantId_DoesNotThrow()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "acme", Description = "matching" });

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_MismatchedTenantId_Throws()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "globex", Description = "wrong tenant" });

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().Throw<TenantIsolationViolationException>();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ModifiedEntity_MatchingTenantId_DoesNotThrow()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);

        // Save first so the entity exists in the database
        db.Orders.Add(new Order { TenantId = "acme", Description = "original" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var order = await db.Orders.FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
        order.Description = "updated";
        // State is now Modified

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ModifiedEntity_MismatchedTenantId_Throws()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);

        // Save an acme order
        db.Orders.Add(new Order { TenantId = "acme", Description = "original" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Manually corrupt TenantId to simulate a cross-tenant mutation attempt
        var order = await db.Orders.FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
        order.TenantId = "globex";

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().Throw<TenantIsolationViolationException>();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DeletedEntity_MismatchedTenantId_Throws()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);

        db.Orders.Add(new Order { TenantId = "acme", Description = "to delete" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var order = await db.Orders.FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
        order.TenantId = "globex"; // Corrupt before delete
        db.Orders.Remove(order);

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().Throw<TenantIsolationViolationException>();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Exception_ContainsCorrectDiagnosticProperties()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "attacker", Description = "cross-tenant" });

        var ex = Assert.Throws<TenantIsolationViolationException>(
            () => TenantWriteGuard<string>.Check(db));

        ex.Kind.Should().Be(TenantIsolationViolationKind.EntityWrite);
        ex.TypeName.Should().Be(nameof(Order));
        ex.OffendingTenantId.Should().Be("attacker");
        ex.ExpectedTenantId.Should().Be("acme");
        ex.Message.Should().Contain("new 'Order' names tenant 'attacker'").And.Contain("Leave TenantId unset");
    }

    [Fact]
    public async Task NonTargetState_IsSkipped()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);

        // Save an acme order so it exists in the DB and is tracked
        db.Orders.Add(new Order { TenantId = "acme", Description = "original" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);


        // Detach the tracked entity and re-attach a new instance with the same key but
        // a different TenantId. Attach sets the entry to Unchanged which exercises the
        // "non-target state" branch.
        var saved = await db.Orders.FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
        db.Entry(saved).State = EntityState.Detached;

        var attachedWithWrongTenant = new Order { Id = saved.Id, TenantId = "globex", Description = saved.Description };
        db.Attach(attachedWithWrongTenant); // State = Unchanged

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_NonStringKey_DoesNotThrow()
    {
        // With a value-type key (Guid), the `tenantId is string` fast-path in IsUnstamped is
        // never matched — this exercises the non-string branch of the unstamped check that
        // the string-keyed tests can't reach.
        var tenantId = Guid.NewGuid();
        var ctx = new GuidTestTenantContext().As(tenantId);

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateGuidContextAsync(ctx, conn);
        db.Orders.Add(new GuidOrder { TenantId = tenantId, Description = "matching" });

        var act = () => TenantWriteGuard<Guid>.Check(db);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task NonTenantEntity_IsSkipped()
    {
        var ctx = TestTenantContext.For("acme");

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateContextAsync(ctx, conn);

        // Add a mapped entity that does not implement ITenantEntity — it is skipped
        db.NonTenants.Add(new NonTenant { Name = "plain" });

        var act = () => TenantWriteGuard<string>.Check(db);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }
}
