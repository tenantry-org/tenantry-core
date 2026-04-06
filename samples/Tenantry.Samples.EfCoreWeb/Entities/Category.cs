// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Non-tenanted reference data — shared across all tenants.
/// Product categories are global and managed by administrators.
/// </summary>
public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Navigation property
    public ICollection<Product> Products { get; set; } = [];
}
