// ReSharper disable PropertyCanBeMadeInitOnly.Global

using Tenantry;
using Tenantry.EfCore;

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Tenant entity, stored in the database like any other entity.
/// It is shared across tenants (it does not implement ITenantEntity) because it is global metadata.
/// In production, you'd likely add: Subscription, BillingInfo, Settings, etc.
/// </summary>
[SharedAcrossTenants]
public class Tenant : TenantDescriptor<string>
{
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public string? Description { get; set; }

    // Example: Could add subscription tier, billing info, etc.
    public string SubscriptionTier { get; set; } = "Free";
}
