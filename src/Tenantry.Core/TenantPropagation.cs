namespace Tenantry;

/// <summary>
/// How Tenantry carries a tenant from one process to another: Tenantry.Http's outgoing requests, Tenantry.AspNetCore's
/// <c>ResolveFromPropagationHeader(...)</c> on the receiving side, and Tenantry.Pro's Hangfire, MassTransit, Quartz.NET
/// and Rebus integrations.
/// </summary>
public static class TenantPropagation
{
    /// <summary>
    /// The name of the HTTP header, the Hangfire job parameter, the MassTransit or Rebus message header, or the
    /// Quartz.NET job data key that carries the tenant's id, formatted by <see cref="TenantIds.Format{TKey}"/>:
    /// <c>tenantry-tenant-id</c>.
    /// </summary>
    public const string HeaderName = "tenantry-tenant-id";
}
