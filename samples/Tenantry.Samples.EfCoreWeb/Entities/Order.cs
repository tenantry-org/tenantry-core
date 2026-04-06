// ReSharper disable PropertyCanBeMadeInitOnly.Global

using Tenantry.Core;
using Tenantry.EfCore;

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Tenanted entity — isolated per tenant.
/// Each tenant sees only their own orders.
/// </summary>
public class Order : TenantScoped<string>
{
    public int Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = "Pending";

    // Navigation property
    public ICollection<OrderItem> Items { get; set; } = [];
}
