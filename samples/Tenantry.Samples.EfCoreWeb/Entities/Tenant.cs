// ReSharper disable PropertyCanBeMadeInitOnly.Global

using Tenantry.Core;

namespace Tenantry.Samples.EfCoreWeb.Entities;

/// <summary>
/// Tenant entity — stored in the database like any other entity.
/// This is NOT tenanted (does not implement ITenantScoped) because it's global metadata.
/// In production, you'd likely add: Subscription, BillingInfo, Settings, etc.
/// </summary>
public class Tenant : TenantDescriptor<string>
{
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public string? Description { get; set; }

    // Example: Could add subscription tier, billing info, etc.
    public string SubscriptionTier { get; set; } = "Free";
}
