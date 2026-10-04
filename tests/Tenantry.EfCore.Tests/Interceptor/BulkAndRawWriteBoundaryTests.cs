using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

// These tests set properties through EF.Property inside ExecuteUpdate on purpose. On EF Core 8 and 9,
// SetProperty takes its selector as a Func nested in ExecuteUpdate's expression tree, so ReSharper reads
// EF.Property there as a client-side call; EF translates it (and EF Core 10 takes an Expression instead).
// ReSharper disable EntityFramework.ClientSideDbFunctionCall

/// <summary>
/// Pins the isolation boundary for writes that bypass <c>SaveChanges</c>: bulk <c>ExecuteUpdate</c> and
/// <c>ExecuteDelete</c> are limited to the current tenant by the query filter and may not change
/// <c>TenantId</c>; <c>IgnoreQueryFilters</c>, <c>SqlQuery</c> and <c>ExecuteSql</c> are deliberately unisolated, and
/// <c>FromSql</c> on a tenant-owned entity is filtered.
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
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);

        var updated = await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "bulk"), cancellationToken: TestContext.Current.CancellationToken);

        updated.Should().Be(1);
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "bulk")]);
    }

    [Fact]
    public async Task ExecuteDelete_OnlyDeletesTheCurrentTenantsRows()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);

        var deleted = await db.Orders.ExecuteDeleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        deleted.Should().Be(1);
        Rows().Should().BeEquivalentTo([("acme", "acme order")]);
    }

    [Fact]
    public async Task ExecuteUpdateAndDelete_WithoutTenant_AffectNoRows()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.AsNone(), _connection);

        var updated = await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, "bulk"), cancellationToken: TestContext.Current.CancellationToken);
        var deleted = await db.Orders.ExecuteDeleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        (updated, deleted).Should().Be((0, 0));
        Rows().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("globex")]
    [InlineData("acme")]
    public async Task ExecuteUpdate_SettingTenantId_ThrowsAndChangesNothing(string newTenantId)
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Awaiting(d => d.Orders.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Description, "moved")
            .SetProperty(o => o.TenantId, newTenantId)))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*")
            .Where(e => e.Kind == TenantIsolationViolationKind.BulkUpdate && e.TypeName == nameof(Order) && e.OffendingTenantId == null);
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdSynchronously_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        db.Invoking(d => d.Orders.ExecuteUpdate(s => s.SetProperty(o => o.TenantId, "globex")))
            .Should().Throw<TenantIsolationViolationException>();
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdWithEfProperty_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Awaiting(d => d.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => EF.Property<string>(o, "TenantId"), "globex")))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdWithEfPropertyAndACapturedName_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);
        var column = nameof(Order.TenantId);

        await db.Awaiting(d => d.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => EF.Property<string>(o, column), "globex")))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdThroughTheInterface_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Awaiting(d => d.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => ((ITenantEntity<string>)o).TenantId, "globex")))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdOnAProjectedEntity_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Awaiting(d => d.Orders
            .Select(o => new { Order = o, o.Description })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order.TenantId, "globex")))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingTenantIdOnAProjectedEntityWithEfProperty_Throws()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Awaiting(d => d.Orders
            .Select(o => new { Order = o, o.Description })
            .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<string>(x.Order, "TenantId"), "globex")))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    // EF Core resolves a setter through the query's projections, so each of these sets Order.TenantId.
    [Theory]
    [InlineData("anonymous")]
    [InlineData("renamed")]
    [InlineData("nested")]
    [InlineData("then filtered")]
    [InlineData("object initializer")]
    [InlineData("join")]
    [InlineData("select many")]
    public async Task ExecuteUpdate_SettingTenantIdThroughAProjectedMember_Throws(string shape)
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        Func<TestDbContext, Task> update = shape switch
        {
            "anonymous" => d => d.Orders.Select(o => new { o.TenantId, o.Description })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, "moved")),
            "renamed" => d => d.Orders.Select(o => new { Owner = o.TenantId, o.Id })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, "moved")),
            "nested" => d => d.Orders.Select(o => new { Inner = new { o.TenantId } })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Inner.TenantId, "moved")),
            "then filtered" => d => d.Orders.Select(o => new { Owner = o.TenantId, o.Description })
                .Where(x => x.Description != "")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, "moved")),
            "object initializer" => d => d.Orders.Select(o => new OrderView { Owner = o.TenantId })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, "moved")),
            "join" => d => d.Orders.Join(d.Orders, a => a.Id, b => b.Id, (a, b) => new { a, Owner = b.TenantId })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, "moved")),
            // The inner query is part of the shape under test, and EF Core translates it.
            // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
            "select many" => d => d.Orders.SelectMany(a => d.Orders.Where(b => b.Id == a.Id), (a, b) => new { a.Id, Owner = b.TenantId })
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, "moved")),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        await db.Awaiting(update).Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*TenantId*Order*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_ThroughAProjectionTheGuardCannotResolve_FailsClosed()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Awaiting(d => d.Orders.GroupBy(o => o.TenantId).Select(g => new { Owner = g.Key })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, "moved")))
            .Should().ThrowAsync<TenantIsolationViolationException>().WithMessage("*cannot check*");
        Rows().Should().BeEquivalentTo([("acme", "acme order"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_SettingOtherPropertiesWithEfPropertyOrOnAProjection_IsAllowed()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => EF.Property<string>(o, "Description"), "by name"), cancellationToken: TestContext.Current.CancellationToken);
        Rows().Should().BeEquivalentTo([("acme", "by name"), ("globex", "globex order")]);

        await db.Orders
            .Select(o => new { Order = o, o.TenantId })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order.Description, x => x.TenantId + " projected"), cancellationToken: TestContext.Current.CancellationToken);
        Rows().Should().BeEquivalentTo([("acme", "acme projected"), ("globex", "globex order")]);

        await db.Orders
            .Join(db.Orders, a => a.Id, b => b.Id, (a, b) => new { a, Owner = b.TenantId })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.a.Description, x => x.Owner + " joined"), cancellationToken: TestContext.Current.CancellationToken);
        Rows().Should().BeEquivalentTo([("acme", "acme joined"), ("globex", "globex order")]);

        var column = nameof(Order.Description);
        await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => EF.Property<string>(o, column), "captured"), cancellationToken: TestContext.Current.CancellationToken);
        Rows().Should().BeEquivalentTo([("acme", "captured"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task ExecuteUpdate_ReadingTenantIdAsAValue_IsAllowed()
    {
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("acme"), _connection);

        await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Description, o => o.TenantId), cancellationToken: TestContext.Current.CancellationToken);

        Rows().Should().BeEquivalentTo([("acme", "acme"), ("globex", "globex order")]);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ExecuteDelete_AffectsEveryTenant()
    {
        // Privileged by design: IgnoreQueryFilters removes the tenant filter, so the bulk delete is unscoped.
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);

        var deleted = await db.Orders.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        deleted.Should().Be(2);
        Rows().Should().BeEmpty();
    }

    [Fact]
    public async Task FromSql_OnATenantOwnedEntity_IsFiltered()
    {
        // EF Core composes the entity's query filters over the SQL, so these read only the current tenant's rows.
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);
        var ct = TestContext.Current.CancellationToken;
        var tenant = "acme";

        (await db.Orders.FromSql($"SELECT * FROM Orders").Select(o => o.Description).ToListAsync(ct))
            .Should().Equal("globex order");
        (await db.Orders.FromSqlRaw("SELECT * FROM Orders").Select(o => o.Description).ToListAsync(ct))
            .Should().Equal("globex order");
        (await db.Orders.FromSqlInterpolated($"SELECT * FROM Orders WHERE TenantId = {tenant}").ToListAsync(ct))
            .Should().BeEmpty("the filter applies to the SQL's own predicate too");
    }

    [Fact]
    public async Task FromSql_ThatCannotBeComposed_OnATenantOwnedEntity_Throws()
    {
        // EF Core must compose the tenant filter over the SQL, and refuses SQL that is not a SELECT, such as a stored
        // procedure call (on SQLite, a PRAGMA).
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);

        await db.Awaiting(context => context.Orders.FromSqlRaw("PRAGMA table_info('Orders')").ToListAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*non-composable SQL*");
    }

    [Fact]
    public async Task SqlQueryAndExecuteSql_AreNotTenantIsolated()
    {
        // Neither maps to an entity type, so no query filter applies, and no interceptor sees what they change.
        await SeedAsync();
        await using var db = await DbContextFactory.CreateContextAsync(_tenant.As("globex"), _connection);
        var ct = TestContext.Current.CancellationToken;
        var tenant = "acme";

        (await db.Database.SqlQuery<string>($"SELECT Description AS Value FROM Orders ORDER BY Id").ToListAsync(ct))
            .Should().Equal("acme order", "globex order");

        (await db.Database.ExecuteSqlAsync($"UPDATE Orders SET Description = 'changed' WHERE TenantId = {tenant}", ct))
            .Should().Be(1);
        (await db.Database.ExecuteSqlRawAsync("DELETE FROM Orders WHERE TenantId = 'acme'", cancellationToken: ct))
            .Should().Be(1);
        Rows().Should().BeEquivalentTo([("globex", "globex order")]);
    }

    private sealed class OrderView
    {
        public string Owner { get; set; } = string.Empty;
    }

    private async Task SeedAsync()
    {
        foreach (var tenant in new[] { "acme", "globex" })
        {
            await using var db = await DbContextFactory.CreateContextAsync(_tenant.As(tenant), _connection);
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
