using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Tenantry.AspNetCore.Extensions;
using Tenantry.Core;
using Testcontainers.MsSql;

namespace Tenantry.IntegrationTests;

/// <summary>
/// End-to-end tests for EF Core-backed tenant resolution.
/// Proves that the full middleware → EF store → tenant context chain works
/// against a real SQL Server database — not just an in-memory store.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EfCoreTenantStoreTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddDbContext<EfStoreDbContext>(options =>
            options.UseSqlServer(_sqlServer.GetConnectionString()));

        builder.Services.AddTenantry<string>(t =>
        {
            t.ResolveFromHeader("X-Tenant-Id");
            t.UseStore<EfStoreTenantStore>();
        });

        _app = builder.Build();
        _app.UseTenantry();

        _app.MapGet("/me", (ITenantContext<string> ctx) =>
            Results.Ok(ctx.CurrentTenant?.Name ?? "(none)"));

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

        var response = await _client.GetAsync("/me");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().Be("\"Acme Corp\""); // Results.Ok serialises as JSON string
    }

    [Fact]
    public async Task EfStore_UnknownTenant_Returns404()
    {
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "not-in-db");

        var response = await _client.GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EfStore_InactiveTenant_Returns404()
    {
        // Tenant exists in the DB, but IsActive = false — store returns null → 404.
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "inactive");

        var response = await _client.GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _sqlServer.DisposeAsync();
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
        CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], cancellationToken: cancellationToken);
        return tenant is { IsActive: true } ? tenant : null;
    }

    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default) =>
        await db.Tenants
            .AsNoTracking()
            .Where(t => t.IsActive)
            .ToListAsync<ITenantDescriptor<string>>(cancellationToken);
}
