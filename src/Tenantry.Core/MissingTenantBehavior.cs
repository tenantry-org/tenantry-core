namespace Tenantry.Core;

/// <summary>
/// Policy for what happens when a tenant-scoped operation runs without a resolved tenant context.
/// Shared across EF Core write isolation and background-job tenant propagation so the behaviour is
/// configured with a single vocabulary.
/// </summary>
public enum MissingTenantBehavior
{
    /// <summary>
    /// Proceed without a tenant, silently. EF Core writes are saved without a stamped
    /// <c>TenantId</c>; a background job runs without a tenant scope.
    /// </summary>
    Allow,

    /// <summary>
    /// Proceed without a tenant, but log a structured warning. The default — surfaces endpoints,
    /// jobs, or middleware ordering that bypassed tenant resolution without failing the operation.
    /// </summary>
    Warn,

    /// <summary>
    /// Fail the operation. EF Core writes throw
    /// <see cref="Exceptions.TenantNotResolvedException" /> before anything is persisted; a
    /// background job throws and is left to the host's retry/error handling.
    /// </summary>
    Reject,

    /// <summary>
    /// Abandon the operation without raising an error. Intended for background-job propagation,
    /// where it means "acknowledge and drop the message/job without running the handler".
    /// For EF Core write isolation there is nothing to abandon, so this behaves like
    /// <see cref="Allow" /> (the save proceeds without a stamped tenant).
    /// </summary>
    Skip
}
