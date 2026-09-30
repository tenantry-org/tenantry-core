namespace Tenantry.EfCore;

/// <summary>
/// What <c>SaveChanges</c> does when it writes tenant-owned entities and no tenant is current. Set with
/// <see cref="EfCoreIsolationOptions.OnMissingTenant"/>. Reads are not affected: they always fail closed.
/// </summary>
public enum MissingTenantBehavior
{
    /// <summary>
    /// Throw <see cref="TenantNotResolvedException" /> before anything is written. The default.
    /// </summary>
    Reject,

    /// <summary>
    /// As <see cref="Allow" />, but log a structured warning. Surfaces code that writes without a tenant, such as
    /// an endpoint or job that bypassed tenant resolution, without failing it.
    /// </summary>
    Warn,

    /// <summary>
    /// Save without a tenant, silently. Updates and deletes are then not checked against a tenant, and a new entity
    /// is saved only if its <c>TenantId</c> is set. For maintenance code that deliberately writes across tenants.
    /// </summary>
    Allow,
}
