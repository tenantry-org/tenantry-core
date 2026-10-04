namespace Tenantry.EfCore;

/// <summary>
/// What a context does when its model has tenant-owned entity types and also entity types that are neither
/// tenant-owned nor marked as shared across tenants. Set with
/// <see cref="EfCoreIsolationOptions.OnUnmarkedEntityType"/>.
/// </summary>
/// <remarks>
/// An entity type that is not tenant-owned is shared by every tenant, which is how Tenantry is meant to be used.
/// <see cref="Warn"/> and <see cref="Reject"/> are for an application that wants each such type marked as shared, so a
/// type left without <see cref="ITenantEntity{TKey}"/> by mistake is found. A context applies its behaviour whenever EF
/// Core compiles one of its queries, and on every save, before anything is read or written. Contexts with different
/// values never share a compiled query. A model with no tenant-owned entity type, such as a database-per-tenant
/// context's, is never checked.
/// </remarks>
public enum UnmarkedEntityTypeBehavior
{
    /// <summary>Use the model as it is. The default.</summary>
    Allow,

    /// <summary>
    /// Use the model, and log a structured warning naming the unmarked entity types, once for each model EF Core builds.
    /// </summary>
    /// <remarks>
    /// That is usually once per context type and set of options. EF Core builds a model again when it drops one from
    /// its cache, and the warning is then logged again.
    /// </remarks>
    Warn,

    /// <summary>
    /// Throw <see cref="TenantIsolationViolationException"/> of kind
    /// <see cref="TenantIsolationViolationKind.ModelConfiguration"/>, naming the unmarked entity types, before the first
    /// query or save. A value outside the enum does the same.
    /// </summary>
    Reject,
}
