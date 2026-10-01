namespace Tenantry;

/// <summary>
/// How Tenantry caches the tenants it reads from the tenant store. Set with <c>tenant.CacheTenants(o =&gt; …)</c>.
/// </summary>
public sealed class TenantStoreCacheOptions
{
    /// <summary>
    /// How long a tenant read from the store is reused before the store is asked again. Defaults to 5 minutes. It
    /// must be positive.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);
}
