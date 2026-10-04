namespace Tenantry.EfCore;

/// <summary>
/// What a context does when its model has tenant-owned entity types and also entity types that are neither
/// tenant-owned nor marked as shared across tenants. Set with <see cref="EfCoreIsolationOptions.OnUnclassifiedEntityType"/>.
/// </summary>
/// <remarks>
/// Tenantry isolates only tenant-owned entity types, so the rows of an unclassified one are read and written for every
/// tenant. The check runs once per model, on the context's first query or save. A model with no tenant-owned entity
/// type, such as a database-per-tenant context's, is never checked.
/// </remarks>
public enum UnclassifiedEntityTypeBehavior
{
    /// <summary>
    /// Throw <see cref="TenantIsolationViolationException"/> of kind
    /// <see cref="TenantIsolationViolationKind.ModelConfiguration"/>, naming every unclassified entity type, before the
    /// first query or save. The default.
    /// </summary>
    Reject,

    /// <summary>As <see cref="Allow"/>, but log a structured warning naming them, once per model.</summary>
    Warn,

    /// <summary>Use the model as it is, silently.</summary>
    Allow,
}
