// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Non-tenanted reference data — shared across all tenants.
/// Products are global catalogue items that any tenant can order.
/// </summary>
public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int CategoryId { get; set; }

    // Navigation properties
    public Category Category { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = [];
}
