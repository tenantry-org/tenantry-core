namespace Tenantry.Core;

/// <summary>
/// Policy for what happens when a tenant-scoped operation runs without a resolved tenant context.
/// Shared across EF Core write isolation and background-job tenant propagation so the behaviour is
/// configured with a single vocabulary.
/// </summary>
public enum MissingTenantBehavior
{
    /// <summary>
    /// Proceed without a tenant, silently. EF Core saves updates and deletes without checking their tenant, and
    /// saves a new entity only if its <c>TenantId</c> is set; a background job runs without a tenant scope.
    /// </summary>
    Allow,

    /// <summary>
    /// As <see cref="Allow" />, but log a structured warning. Surfaces endpoints, jobs, or middleware
    /// ordering that bypassed tenant resolution without failing the operation. The default for job and
    /// message propagation in Tenantry.Pro.
    /// </summary>
    Warn,

    /// <summary>
    /// Fail the operation. EF Core writes throw
    /// <see cref="Exceptions.TenantNotResolvedException" /> before anything is persisted; a
    /// background job throws and is left to the host's retry/error handling. The default for EF Core
    /// write isolation.
    /// </summary>
    Reject,

    /// <summary>
    /// Abandon the operation without raising an error. Only for background-job propagation, where the
    /// handler does not run; what happens to the job or message then depends on the host (see each
    /// integration's guide). EF Core write isolation does not accept it: setting it there throws
    /// <see cref="ArgumentOutOfRangeException" />.
    /// </summary>
    Skip
}
