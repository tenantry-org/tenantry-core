using AwesomeAssertions;
using Tenantry;

namespace Tenantry.EfCore.Tests;

/// <summary>A concrete entity using the TenantEntity base class.</summary>
internal sealed class Invoice : TenantEntity<string>
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class TenantEntityBaseClassTests
{
    [Fact]
    public void TenantEntity_TenantId_DefaultsToDefault()
    {
        Invoice invoice = new();

        // TenantId is `default!` which for string is null (suppressed)
        // In practice it is null; the interceptor stamps it on Save.
        (invoice.TenantId is null or "").Should().BeTrue();
    }

    [Fact]
    public void TenantEntity_ImplementsITenantEntity()
    {
        Invoice invoice = new();

        invoice.Should().BeAssignableTo<ITenantEntity<string>>();
    }

    [Fact]
    public async Task TenantEntity_IsStampedAndFiltered()
    {
        var tenant = TestTenantContext.For("acme");
        await using var connection = DbContextFactory.CreateSharedConnection();
        await using InvoicesContext db = new(DbContextFactory.Options<InvoicesContext>(tenant, connection));
        await db.Database.EnsureCreatedAsync();

        db.Invoices.Add(new Invoice { Description = "test" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.Invoices.SingleAsync()).TenantId.Should().Be("acme");
        tenant.As("globex");
        (await db.Invoices.CountAsync()).Should().Be(0);
    }

    private sealed class InvoicesContext(DbContextOptions<InvoicesContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
    }
}
