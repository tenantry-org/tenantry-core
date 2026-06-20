using AwesomeAssertions;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Verifies that the interceptor prevents cross-tenant modifications before any data
/// is written to the database.
/// </summary>
public sealed class InterceptorCrossTenantWriteTests
{
    [Fact]
    public async Task SaveChanges_WhenModifyingEntityWithDifferentTenantId_ThrowsIsolationViolation()
    {
        // Arrange — seed an "acme" entity
        var ctx = TestTenantContext.For("acme");
        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        Order order = new() { Description = "Acme order" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var savedId = order.Id;

        // Clear the change tracker so we can Attach a different object with the same key.
        // In a real app the attacker would use a fresh context; clearing here is the
        // in-process equivalent.
        db.ChangeTracker.Clear();

        // Switch scope to "globex" but try to modify an entity with TenantId = "acme"
        ctx.As("globex");

        Order attackerEntity = new()
        {
            Id = savedId,
            TenantId = "acme", // belongs to acme
            Description = "Modified by attacker",
        };

        db.Orders.Attach(attackerEntity);
        db.Entry(attackerEntity).State = EntityState.Modified;

        // Act & Assert — throws before any DB write
        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("*acme*")
            .WithMessage("*globex*");
        
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveChanges_WhenDeletingEntityWithDifferentTenantId_ThrowsIsolationViolation()
    {
        // Arrange
        var ctx = TestTenantContext.For("globex");
        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        Order foreignOrder = new()
        {
            Id = 99,
            TenantId = "acme", // belongs to acme, not globex
            Description = "Not mine",
        };

        db.Orders.Attach(foreignOrder);
        db.Orders.Remove(foreignOrder);

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantIsolationViolationException>();
        
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveChanges_WhenModifyingOwnEntity_Succeeds()
    {
        // Arrange — modifying own entity should NOT throw
        var ctx = TestTenantContext.For("acme");
        await using var conn = DbContextFactory.CreateSharedConnection();
        var db = await DbContextFactory.CreateInterceptorContextAsync(ctx, conn);

        Order order = new() { Description = "Acme order" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        order.Description = "Updated description";

        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
        
        await db.DisposeAsync();
    }
}
