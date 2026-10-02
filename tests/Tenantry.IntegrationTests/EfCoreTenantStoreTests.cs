using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.IntegrationTests.Providers;

namespace Tenantry.IntegrationTests;

/// <summary>
/// End-to-end tests for EF Core-backed tenant resolution.
/// Proves that the full middleware → EF store → tenant context chain works
/// against a real SQL Server database — not just an in-memory store.
/// </summary>
/// <remarks>Each test gets its own database in the run's SQL Server container.</remarks>
public sealed class EfCoreTenantStoreTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private readonly string _connectionString = sqlServer.WithDatabase($"store_{Guid.NewGuid():N}");

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddDbContext<EfStoreDbContext>(options =>
            options.UseSqlServer(_connectionString));

        builder.Services.AddTenantry<string>(t =>
        {
            t.ResolveFromHeader("X-Tenant-Id");
            t.UseStore<EfStoreTenantStore>();
            // The store returns inactive tenants too; the validator refuses them.
            t.ValidateTenantAccess((_, tenant) => tenant is EfStoreTenant { IsActive: true });
        });

        _app = builder.Build();
        _app.UseTenantry();

        _app.MapGet("/me", (ITenantContext<string> ctx) =>
            Results.Ok(ctx.CurrentTenant?.Name ?? "(none)"))
            .RequireTenant();

        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfStoreDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.AddRange(
            new EfStoreTenant { TenantId = "acme", Name = "Acme Corp", IsActive = true },
            new EfStoreTenant { TenantId = "inactive", Name = "Gone Co", IsActive = false });
        await db.SaveChangesAsync();

        _client = _app.GetTestClient();
    }

    [Fact]
    public async Task EfStore_KnownActiveTenant_ResolvesFromDatabase()
    {
        // The tenant store hits SQL Server on every request (scoped, no cache).
        // The middleware must resolve "Acme Corp" from the DB, not from in-memory config.
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "acme");

        var response = await _client.GetAsync("/me", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("\"Acme Corp\""); // Results.Ok serialises as JSON string
    }

    [Fact]
    public async Task EfStore_UnknownTenant_GetsTheAccessDeniedResponse()
    {
        // With an access validator, a tenant that does not exist is refused like one that is not allowed.
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "not-in-db");

        var response = await _client.GetAsync("/me", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EfStore_InactiveTenant_IsRefusedByValidator()
    {
        // Tenant exists in the DB with IsActive = false: the store returns it and the validator → 403.
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "inactive");

        var response = await _client.GetAsync("/me", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EfStore_ListsInactiveTenants()
    {
        // Tools that maintain every tenant's database (migrations, provisioning) enumerate the store.
        using var scope = _app.Services.CreateScope();
        var tenants = await scope.ServiceProvider.GetRequiredService<ITenantLookup<string>>()
            .GetAllTenantsAsync(TestContext.Current.CancellationToken);

        tenants.Select(t => t.TenantId).Should().BeEquivalentTo("acme", "inactive");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}

// ── In-test infrastructure ──────────────────────────────────────────────────

internal sealed class EfStoreTenant : ITenantDescriptor<string>
{
    public string TenantId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

internal sealed class EfStoreDbContext(DbContextOptions<EfStoreDbContext> options) : DbContext(options)
{
    public DbSet<EfStoreTenant> Tenants => Set<EfStoreTenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<EfStoreTenant>(b =>
        {
            b.HasKey(t => t.TenantId);
            b.Property(t => t.TenantId).HasMaxLength(64);
            b.Property(t => t.Name).HasMaxLength(200);
        });
    }
}

internal sealed class EfStoreTenantStore(EfStoreDbContext db) : ITenantStore<string>
{
    public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default) =>
        await db.Tenants.FindAsync([tenantId], cancellationToken: cancellationToken);

    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default) =>
        await db.Tenants
            .AsNoTracking()
            .ToListAsync<ITenantDescriptor<string>>(cancellationToken);
}
