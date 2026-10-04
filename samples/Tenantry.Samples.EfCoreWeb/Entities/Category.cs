// ReSharper disable PropertyCanBeMadeInitOnly.Global

using Tenantry.EfCore;

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Reference data every tenant shares.
/// Product categories are global and managed by administrators.
/// </summary>
[SharedAcrossTenants]
public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Navigation property
    public ICollection<Product> Products { get; set; } = [];
}
