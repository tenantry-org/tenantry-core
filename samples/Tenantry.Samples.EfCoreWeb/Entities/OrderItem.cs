// ReSharper disable PropertyCanBeMadeInitOnly.Global

using Tenantry;

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Tenant-owned entity, isolated per tenant.
/// Order items inherit tenant isolation from their parent Order.
/// Demonstrates relationships between tenant-owned entities, and navigation to
/// reference data every tenant shares (Product).
/// </summary>
public class OrderItem : TenantEntity<string>
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    // Navigation properties
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
