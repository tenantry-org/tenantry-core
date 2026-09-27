using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Tenantry.Core.Exceptions;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Pins the isolation boundary for writes that bypass <c>SaveChanges</c>: bulk <c>ExecuteUpdate</c> and
/// <c>ExecuteDelete</c> are limited to the current tenant by the query filter and may not change
/// <c>TenantId</c>; <c>IgnoreQueryFilters</c> and raw SQL are deliberately unisolated.
/// </summary>
public sealed class BulkAndRawWriteBoundaryTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ExecuteUpdate_OnlyUpdatesTheCurrentTenantsRows()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("globex"), _connection);

        var updated = await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "bulk"));

        updated.Should().Be(1);
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "bulk")]);
    }

    [Fact]
    public async Task ExecuteDelete_OnlyDeletesTheCurrentTenantsRows()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("globex"), _connection);

        var deleted = await db.Orders.ExecuteDeleteAsync();

        deleted.Should().Be(1);
        Rows().Should().BeEquivalentTo([("acme", "acme order")]);
    }

    [Fact]
    public async Task ExecuteUpdateAndDelete_WithoutTenant_AffectNoRows()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.AsNone(), _connection);

        var updated = await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "bulk"));
        var deleted = await db.Orders.ExecuteDeleteAsync();

        (updated, deleted).Should().Be((0, 0));
        Rows().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("globex")]
    [InlineData("acme")]
    public async Task ExecuteUpdate_SettingTenantId_ThrowsAndChangesNothing(string newTenantId)
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("acme"), _connection);

        Func<Task> act = () => db.Orders.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Description, "moved")
            .SetProperty(o => o.TenantId, newTenantId));

        await act.Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdSynchronously_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("acme"), _connection);

        var act = () => db.Orders.ExecuteUpdate(s => s.SetProperty(o => o.TenantId, "globex"));

        act.Should().Throw<TenantIsolationViolationException>();
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_ReadingTenantIdAsAValue_IsAllowed()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("acme"), _connection);

        await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, o => o.TenantId));

        Rows().Should().BeEquivalentTo([("acme", "acme"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdThroughTheBaseClassContext_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateBaseClassContextAsync(_tenant.As("acme"), _connection);

        Func<Task> act = () => db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.TenantId, "globex"));

        await act.Should().ThrowAsync<TenantIsolationViolationException>();
    }

    [Fact]
    public async Task IgnoreQueryFilters_ExecuteDelete_AffectsEveryTenant()
    {
        // Privileged by design: IgnoreQueryFilters removes the tenant filter, so the bulk delete is unscoped.
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("globex"), _connection);

        var deleted = await db.Orders.IgnoreQueryFilters().ExecuteDeleteAsync();

        deleted.Should().Be(2);
        Rows().Should().BeEmpty();
    }

    [Fact]
    public async Task RawSql_IsNotTenantIsolated()
    {
        // Raw SQL is outside EF Core's query pipeline: neither the filter nor the interceptors apply.
        await SeedAsync();
        await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As("globex"), _connection);

        var deleted = await db.Database.ExecuteSqlRawAsync("DELETE FROM Orders WHERE TenantId = 'acme'");

        deleted.Should().Be(1);
        Rows().Should().BeEquivalentTo([("globex", "globex order")]);
    }

    private async Task SeedAsync()
    {
        foreach (var tenant in new[] { "acme", "globex" })
        {
            await using var db = await DbContextFactory.CreateInterceptorContextAsync(_tenant.As(tenant), _connection);
            db.Orders.Add(new Order { Description = $"{tenant} order" });
            await db.SaveChangesAsync();
        }
    }

    private List<(string TenantId, string Description)> Rows()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT TenantId, Description FROM Orders ORDER BY Id";
        using var reader = command.ExecuteReader();
        List<(string, string)> rows = [];

        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }
}
