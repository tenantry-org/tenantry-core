using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Verifies that <c>UPDATE</c> and <c>DELETE</c> only affect rows stored under the current tenant, even when
/// the caller supplies another tenant's primary key together with the current tenant's <c>TenantId</c>.
/// </summary>
public sealed class InterceptorPersistedOwnershipTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData("update")]
    [InlineData("attach-modified")]
    [InlineData("remove")]
    public async Task ForgedTenantIdOnAnotherTenantsKey_MatchesNoRow_AndLeavesTheRowUnchanged(string write)
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);

        // The caller knows acme's primary key and claims the row is its own.
        Order forged = new() { Id = acmeOrderId, TenantId = "globex", Description = "overwritten" };

        switch (write)
        {
            case "update":
                db.Orders.Update(forged);
                break;
            case "attach-modified":
                db.Orders.Attach(forged);
                db.Entry(forged).State = EntityState.Modified;
                break;
            case "remove":
                db.Orders.Remove(forged);
                break;
        }

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        ReadRow(acmeOrderId).Should().Be(("acme", "acme order"));
    }

    [Fact]
    public async Task EntityLoadedUnderAnotherTenant_ModifiedAfterScopeSwitch_ThrowsIsolationViolation()
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);
        var order = await db.Orders.SingleAsync(o => o.Id == acmeOrderId, cancellationToken: TestContext.Current.CancellationToken);

        _tenant.As("globex");
        order.TenantId = "globex";
        order.Description = "moved";

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("*acme*")
            .WithMessage("*globex*");
        ReadRow(acmeOrderId).Should().Be(("acme", "acme order"));
    }

    [Fact]
    public async Task EntityLoadedUnderAnotherTenant_RemovedAfterScopeSwitch_ThrowsIsolationViolation()
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);
        var order = await db.Orders.SingleAsync(o => o.Id == acmeOrderId, cancellationToken: TestContext.Current.CancellationToken);

        _tenant.As("globex");
        db.Orders.Remove(order);

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        ReadRow(acmeOrderId).Should().Be(("acme", "acme order"));
    }

    [Fact]
    public async Task ChangingTenantIdOfOwnEntity_ThrowsIsolationViolation()
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);
        var order = await db.Orders.SingleAsync(o => o.Id == acmeOrderId, cancellationToken: TestContext.Current.CancellationToken);

        order.TenantId = "globex";

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        ReadRow(acmeOrderId).Should().Be(("acme", "acme order"));
    }

    [Fact]
    public async Task RemovingEntityWithNullTenantId_ThrowsIsolationViolation()
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        db.Orders.Remove(new Order { Id = acmeOrderId, TenantId = null! });

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<TenantIsolationViolationException>();
        ReadRow(acmeOrderId).Should().NotBeNull();
    }

    [Fact]
    public async Task DetachedUpdateOfOwnRow_Succeeds()
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        db.Orders.Update(new Order { Id = acmeOrderId, TenantId = "acme", Description = "updated" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReadRow(acmeOrderId).Should().Be(("acme", "updated"));
    }

    [Fact]
    public async Task DetachedRemoveOfOwnRow_Succeeds()
    {
        var acmeOrderId = await SeedAcmeOrderAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        db.Orders.Remove(new Order { Id = acmeOrderId, TenantId = "acme" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReadRow(acmeOrderId).Should().BeNull();
    }

    [Fact]
    public async Task UseTenantry_MarksTenantIdAsConcurrencyToken()
    {
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        var tenantId = db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.TenantId))!;

        tenantId.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public async Task GuidKeys_ForgedTenantIdOnAnotherTenantsKey_MatchesNoRow()
    {
        Guid acme = Guid.NewGuid(), globex = Guid.NewGuid();
        GuidTestTenantContext tenant = new();
        await using var connection = DbContextFactory.CreateSharedConnection();

        int acmeOrderId;
        await using (var seed = await DbContextFactory.CreateGuidContextAsync(tenant.As(acme), connection))
        {
            GuidOrder order = new() { Description = "acme order" };
            seed.Orders.Add(order);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            acmeOrderId = order.Id;
        }

        await using var db = await DbContextFactory.CreateGuidContextAsync(tenant.As(globex), connection);
        db.Orders.Update(new GuidOrder { Id = acmeOrderId, TenantId = globex, Description = "overwritten" });

        await db.Awaiting(d => d.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        tenant.As(acme);
        await using var check = await DbContextFactory.CreateGuidContextAsync(tenant, connection);
        (await check.Orders.SingleAsync(o => o.Id == acmeOrderId, cancellationToken: TestContext.Current.CancellationToken)).Description.Should().Be("acme order");
    }

    private async Task<int> SeedAcmeOrderAsync()
    {
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);
        Order order = new() { Description = "acme order" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private (string TenantId, string Description)? ReadRow(int id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT TenantId, Description FROM Orders WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }
}
