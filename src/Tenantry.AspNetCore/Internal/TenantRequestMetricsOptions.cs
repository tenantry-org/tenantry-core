namespace Tenantry.AspNetCore.Internal;

/// <summary>Whether <c>app.UseTenantry()</c> tags ASP.NET Core's request metrics with the tenant, and with what.</summary>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
internal sealed class TenantRequestMetricsOptions<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    /// <summary>Set by <c>tenant.TagRequestMetrics()</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>The tag's value for a tenant, or null to leave it off; without it, the tenant id.</summary>
    public Func<ITenantDescriptor<TKey>, string?>? GetTagValue { get; set; }
}
