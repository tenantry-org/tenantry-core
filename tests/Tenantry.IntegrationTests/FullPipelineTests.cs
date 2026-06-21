using System.ComponentModel.DataAnnotations;
using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore;
using Testcontainers.MsSql;

namespace Tenantry.IntegrationTests;

/// <summary>
/// End-to-end tests: HTTP middleware → tenant resolution → EF Core isolation.
/// Uses TestServer + SQL Server (via Testcontainers) to exercise the full stack
/// against a real relational database.
/// </summary>
/// <remarks>
/// xUnit creates a new class instance per [Fact], so each test gets its own fresh
/// SQL Server container and database — no inter-test data leakage.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class FullPipelineTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();

        // Force Development so the Developer Exception Page turns unhandled exceptions
        // (isolation violations, NOT NULL failures) into 500s. Without this, the host
        // defaults to Production in CI — no exception handler is registered, and TestServer
        // rethrows straight to the HttpClient instead of returning 500.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddTenantry<string>(t =>
        {
            t.ResolveFromHeader("X-Tenant-Id");
            t.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex LLC" },
            ]);
            t.AddEfCoreIsolation(options =>
            {
                options.StrictIsolation = true;
            });
        });

        builder.Services.AddDbContext<IntegrationOrderDbContext>((sp, options) =>
        {
            options.UseSqlServer(_sqlServer.GetConnectionString());
            options.AddTenantInterceptors(sp);
        });

        _app = builder.Build();
        _app.UseTenantry();

        // Endpoint: list current tenant's orders (query filter applied automatically)
        _app.MapGet("/orders", async (IntegrationOrderDbContext db) =>
        {
            var orders = await db.Orders
                .AsNoTracking()
                .Select(o => new IntegrationOrderResponse(o.Id, o.TenantId, o.Description))
                .ToListAsync();
            return orders;
        });

        // Endpoint: create an order (interceptor stamps TenantId)
        _app.MapPost("/orders", async (IntegrationOrderDbContext db, IntegrationCreateOrderRequest req) =>
        {
            IntegrationOrder order = new() { Description = req.Description };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return new IntegrationOrderResponse(order.Id, order.TenantId, order.Description);
        });

        // Endpoint: global reference data — no tenant filter (Labels don't implement ITenantScoped)
        _app.MapGet("/labels", async (IntegrationOrderDbContext db) =>
        {
            var labels = await db.Labels
                .AsNoTracking()
                .Select(l => l.Text)
                .ToListAsync();
            return labels;
        });

        // Endpoint: admin cross-tenant query — bypasses query filter
        _app.MapGet("/orders/all", async (IntegrationOrderDbContext db) =>
        {
            var orders = await db.Orders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(o => new IntegrationOrderResponse(o.Id, o.TenantId, o.Description))
                .ToListAsync();
            return orders;
        });

        // Endpoint: admin delete — loads with IgnoreQueryFilters, so the entity is found even if
        // it belongs to a different tenant; interceptor still validates TenantId on SaveChanges.
        _app.MapDelete("/orders/{id:int}/admin", async (int id, IntegrationOrderDbContext db) =>
        {
            var order = await db.Orders
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
                return Results.NotFound();

            db.Orders.Remove(order);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Endpoint: create with an explicit TenantId — used to test strict-mode spoofing detection
        _app.MapPost("/orders/explicit-tenant", async (IntegrationOrderDbContext db, IntegrationCreateOrderWithTenantRequest req) =>
        {
            IntegrationOrder order = new() { TenantId = req.TenantId, Description = req.Description };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return new IntegrationOrderResponse(order.Id, order.TenantId, order.Description);
        });

        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<IntegrationOrderDbContext>();
        await seedDb.Database.EnsureCreatedAsync();
        seedDb.Labels.Add(new IntegrationLabel { Text = "Global notice" });
        await seedDb.SaveChangesAsync();

        _client = _app.GetTestClient();
    }

    // ── Core isolation ───────────────────────────────────────────────────────

    [Fact]
    public async Task PostOrder_StampsTenantId_AndGetReturnsOnlyThatTenantsOrders()
    {
        using var acmeClient = _app.GetTestClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        var postResponse = await acmeClient.PostAsJsonAsync("/orders", new IntegrationCreateOrderRequest("Acme Widget"));
        postResponse.IsSuccessStatusCode.Should().BeTrue();

        var created = await postResponse.Content.ReadFromJsonAsync<IntegrationOrderResponse>();
        created.Should().NotBeNull();
        created.TenantId.Should().Be("acme");

        var acmeOrders = await acmeClient.GetFromJsonAsync<List<IntegrationOrderResponse>>("/orders");
        acmeOrders.Should().HaveCount(1).And.AllSatisfy(o => o.TenantId.Should().Be("acme"));

        using var globexClient = _app.GetTestClient();
        globexClient.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");
        var globexOrders = await globexClient.GetFromJsonAsync<List<IntegrationOrderResponse>>("/orders");
        globexOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_UnknownTenant_Returns404()
    {
        using var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "no-such-tenant");

        var response = await client.GetAsync("/orders");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConcurrentRequests_DifferentTenants_EachSeeOnlyTheirData()
    {
        using var acmeSetup = _app.GetTestClient();
        acmeSetup.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        await acmeSetup.PostAsJsonAsync("/orders", new IntegrationCreateOrderRequest("Acme Widget"));

        using var globexSetup = _app.GetTestClient();
        globexSetup.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");
        await globexSetup.PostAsJsonAsync("/orders", new IntegrationCreateOrderRequest("Globex Gadget"));

        using var acmeClient = _app.GetTestClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        using var globexClient = _app.GetTestClient();
        globexClient.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");

        var responses = await Task.WhenAll(
            acmeClient.GetAsync("/orders"),
            globexClient.GetAsync("/orders"));

        var acmeOrders =
            await responses[0].Content.ReadFromJsonAsync<List<IntegrationOrderResponse>>();
        var globexOrders =
            await responses[1].Content.ReadFromJsonAsync<List<IntegrationOrderResponse>>();

        // AsyncLocal must not have leaked between concurrent async contexts
        acmeOrders.Should().NotBeNull().And.AllSatisfy(o => o.TenantId.Should().Be("acme"));
        globexOrders.Should().NotBeNull().And.AllSatisfy(o => o.TenantId.Should().Be("globex"));
        acmeOrders.Should().NotIntersectWith(globexOrders);
    }

    // ── No-tenant context ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveChanges_NoTenantContext_DbRejectsNullTenantId()
    {
        // No header → middleware continues without setting tenant context.
        // The interceptor logs a warning but does NOT throw itself.
        // SQL Server enforces NOT NULL on TenantId, so the insert is correctly
        // rejected at the DB layer with a DbUpdateException → 500.
        using var client = _app.GetTestClient();

        var postResponse = await client.PostAsJsonAsync("/orders",
            new IntegrationCreateOrderRequest("No-tenant order"));

        postResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    // ── Non-tenanted (global) entities ───────────────────────────────────────

    [Fact]
    public async Task NonTenantedEntity_VisibleToAllTenants()
    {
        // IntegrationLabel doesn't implement ITenantScoped<string>, so no query filter
        // is applied. It must be visible regardless of the tenant context.

        using var acmeClient = _app.GetTestClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        var acmeLabels = await acmeClient.GetFromJsonAsync<List<string>>("/labels");
        acmeLabels.Should().ContainSingle().Which.Should().Be("Global notice");

        // No tenant header — endpoint still returns data
        using var anonClient = _app.GetTestClient();
        var anonLabels = await anonClient.GetFromJsonAsync<List<string>>("/labels");
        anonLabels.Should().ContainSingle().Which.Should().Be("Global notice");
    }

    // ── Admin / IgnoreQueryFilters ────────────────────────────────────────────

    [Fact]
    public async Task AdminQuery_IgnoreQueryFilters_ReturnsAllTenantsOrders()
    {
        using var acmeClient = _app.GetTestClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        await acmeClient.PostAsJsonAsync("/orders", new IntegrationCreateOrderRequest("Acme Widget"));

        using var globexClient = _app.GetTestClient();
        globexClient.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");
        await globexClient.PostAsJsonAsync("/orders", new IntegrationCreateOrderRequest("Globex Gadget"));

        // Admin query — no tenant header, bypasses filter
        using var adminClient = _app.GetTestClient();
        var allOrders = await adminClient.GetFromJsonAsync<List<IntegrationOrderResponse>>("/orders/all");

        allOrders.Should().HaveCount(2);
        allOrders.Should().Contain(o => o.TenantId == "acme");
        allOrders.Should().Contain(o => o.TenantId == "globex");
    }

    // ── Cross-tenant write protection ─────────────────────────────────────────

    [Fact]
    public async Task CrossTenantDelete_Returns500()
    {
        // Acme creates an order
        using var acmeClient = _app.GetTestClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");
        var postResponse = await acmeClient.PostAsJsonAsync("/orders", new IntegrationCreateOrderRequest("Acme Widget"));
        var order = await postResponse.Content.ReadFromJsonAsync<IntegrationOrderResponse>();
        order.Should().NotBeNull();

        // Globex tries to delete Acme's order via the admin endpoint.
        // The endpoint loads it with IgnoreQueryFilters() (finds it), then calls Remove.
        // Interceptor sees Deleted entity with TenantId="acme" but current="globex" → throws.
        using var globexClient = _app.GetTestClient();
        globexClient.DefaultRequestHeaders.Add("X-Tenant-Id", "globex");
        var deleteResponse = await globexClient.DeleteAsync($"/orders/{order.Id}/admin");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task StrictMode_SpoofedTenantId_Returns500()
    {
        // Client authenticates as Acme but provides a body claiming ownership by Globex.
        // StrictIsolationValidator sees an Added entity with TenantId="globex" (non-null, non-current)
        // and throws before any DB write operation.
        using var acmeClient = _app.GetTestClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await acmeClient.PostAsJsonAsync("/orders/explicit-tenant",
            new IntegrationCreateOrderWithTenantRequest("globex", "Spoofed order"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    // ── Resolver chaining ─────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleResolvers_HeaderNullQueryStringWins_SecondResolverUsed()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddTenantry<string>(t =>
        {
            t.ResolveFromHeader("X-Tenant-Id");
            t.ResolveFromQueryString();
            t.UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme Corp" },
            ]);
            t.AddEfCoreIsolation(options =>
            {
                options.StrictIsolation = true;
            });
        });

        builder.Services.AddDbContext<IntegrationOrderDbContext>((sp, options) =>
        {
            options.UseSqlServer(_sqlServer.GetConnectionString());
            options.AddTenantInterceptors(sp);
        });

        await using var app = builder.Build();
        app.UseTenantry();
        app.MapGet("/tenant", (ITenantContext<string> ctx) => ctx.CurrentTenantId ?? "(none)");
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegrationOrderDbContext>().Database.EnsureCreatedAsync();

        using var client = app.GetTestClient();
        var response = await client.GetAsync("/tenant?tenantId=acme");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("acme");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }
}

// ── In-test models ──────────────────────────────────────────────────────────

internal sealed class IntegrationOrder : ITenantScoped<string>
{
    public int Id { get; init; }

    [MaxLength(64)]
    public string TenantId { get; set; } = null!;

    [MaxLength(200)]
    public string Description { get; init; } = string.Empty;
}

/// <summary>Non-tenanted global reference entity — no ITenantScoped, no query filter.</summary>
internal sealed class IntegrationLabel
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Text { get; set; } = string.Empty;
}

internal sealed class IntegrationOrderDbContext(
    DbContextOptions<IntegrationOrderDbContext> options,
    ITenantContext<string> tenantContext)
    : DbContext(options), ITenantAwareDbContext<string>
{
    public DbSet<IntegrationOrder> Orders => Set<IntegrationOrder>();
    public DbSet<IntegrationLabel> Labels => Set<IntegrationLabel>();
    public string? CurrentTenantId => tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Only ITenantScoped<string> types get a query filter — IntegrationLabel is excluded.
        modelBuilder.ApplyTenantFilters<string, IntegrationOrderDbContext>(this);
    }
}

internal record IntegrationOrderResponse(int Id, string TenantId, string Description);
internal record IntegrationCreateOrderRequest(string Description);
internal record IntegrationCreateOrderWithTenantRequest(string TenantId, string Description);
