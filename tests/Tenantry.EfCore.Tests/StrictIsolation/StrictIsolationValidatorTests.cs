using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tenantry.Core.Exceptions;
using Tenantry.EfCore.Internal;
using Tenantry.EfCore.Tests.Infrastructure;

namespace Tenantry.EfCore.Tests.StrictIsolation;

public sealed class StrictIsolationValidatorTests
{
    private static StrictIsolationValidator<string> MakeValidator() =>
        new(NullLogger<StrictIsolationValidator<string>>.Instance);

    [Fact]
    public async Task NoTenantContext_DoesNotThrow()
    {
        var ctx = TestTenantContext.Empty();
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "acme", Description = "test" });

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_DefaultTenantId_IsSkipped()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);
        // TenantId = null (the actual default for string) — interceptor will stamp it
        db.Orders.Add(new Order { TenantId = null!, Description = "unstamped" });

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_MatchingTenantId_DoesNotThrow()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "acme", Description = "matching" });

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddedEntity_MismatchedTenantId_Throws()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "globex", Description = "wrong tenant" });

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().Throw<TenantIsolationViolationException>();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ModifiedEntity_MatchingTenantId_DoesNotThrow()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        // Save first so the entity exists in the database
        db.Orders.Add(new Order { TenantId = "acme", Description = "original" });
        await db.SaveChangesAsync();

        var order = await db.Orders.FirstAsync();
        order.Description = "updated";
        // State is now Modified

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().NotThrow();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ModifiedEntity_MismatchedTenantId_Throws()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        // Save an acme order
        db.Orders.Add(new Order { TenantId = "acme", Description = "original" });
        await db.SaveChangesAsync();

        // Manually corrupt TenantId to simulate a cross-tenant mutation attempt
        var order = await db.Orders.FirstAsync();
        order.TenantId = "globex";

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().Throw<TenantIsolationViolationException>();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DeletedEntity_MismatchedTenantId_Throws()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        db.Orders.Add(new Order { TenantId = "acme", Description = "to delete" });
        await db.SaveChangesAsync();

        var order = await db.Orders.FirstAsync();
        order.TenantId = "globex"; // Corrupt before delete
        db.Orders.Remove(order);

        var act = () => validator.Validate(db.ChangeTracker.Entries(), ctx);

        act.Should().Throw<TenantIsolationViolationException>();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Exception_ContainsCorrectDiagnosticProperties()
    {
        var ctx = TestTenantContext.For("acme");
        var validator = MakeValidator();

        await using var conn = DbContextFactory.CreateSharedConnection();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);
        db.Orders.Add(new Order { TenantId = "attacker", Description = "cross-tenant" });

        var ex = Assert.Throws<TenantIsolationViolationException>(
            () => validator.Validate(db.ChangeTracker.Entries(), ctx));

        ex.EntityTypeName.Should().Be(nameof(Order));
        ex.OffendingTenantId.Should().Be("attacker");
        ex.ExpectedTenantId.Should().Be("acme");
    }
}
