// ReSharper disable PropertyCanBeMadeInitOnly.Global

using Tenantry.Core;
using Tenantry.EfCore;

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Tenanted entity — isolated per tenant.
/// Order items inherit tenant isolation from their parent Order.
/// Demonstrates relationships between tenanted entities and cross-boundary
/// navigation to non-tenanted reference data (Product).
/// </summary>
public class OrderItem : TenantScoped<string>
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
