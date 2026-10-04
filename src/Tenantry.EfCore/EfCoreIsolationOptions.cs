namespace Tenantry.EfCore;

/// <summary>
/// Options for EF Core tenant isolation: the application's, set with
/// <c>tenant.ConfigureEfCoreIsolation(options =&gt; …)</c>, or one context's, set with
/// <c>options.UseTenantry(o =&gt; …)</c>.
/// </summary>
/// <remarks>
/// Reads always fail closed, a new entity that names another tenant is always rejected, and <c>ExecuteUpdate</c>
/// can never set <c>TenantId</c>. Whenever a tenant is current, <c>Modified</c> and <c>Deleted</c> entities must
/// belong to it, checked before saving and again in each <c>UPDATE</c> and <c>DELETE</c>. These options decide what
/// happens to writes without a tenant, to saves without a transaction, and to entity types neither tenant-owned nor
/// shared. <c>Database.SqlQuery</c>, <c>ExecuteSql</c> and <c>IgnoreQueryFilters()</c> are outside Tenantry's
/// isolation.
/// </remarks>
public sealed class EfCoreIsolationOptions
{
    /// <summary>
    /// What a save does when it writes tenant-owned entities and no tenant is current. Defaults to
    /// <see cref="MissingTenantBehavior.Reject"/>. Saves that write no tenant-owned entity are never affected.
    /// </summary>
    public MissingTenantBehavior OnMissingTenant { get; set; } = MissingTenantBehavior.Reject;

    /// <summary>
    /// What a save does when <c>AutoTransactionBehavior</c> is <c>Never</c> and some of its rows depend on another
    /// statement's tenant check (owned rows in their own table, or an entity split across tables). Defaults to
    /// <see cref="SaveWithoutTransactionBehavior.UseTransaction"/>.
    /// </summary>
    public SaveWithoutTransactionBehavior OnSaveWithoutTransaction { get; set; } = SaveWithoutTransactionBehavior.UseTransaction;

    /// <summary>
    /// What a context does when its model has tenant-owned entity types and also entity types that are neither
    /// tenant-owned nor marked as shared across tenants. Defaults to <see cref="UnclassifiedEntityTypeBehavior.Reject"/>.
    /// </summary>
    /// <remarks>
    /// Mark an entity type whose rows every tenant shares with <see cref="SharedAcrossTenantsAttribute"/> or
    /// <c>IsSharedAcrossTenants()</c>. <see cref="TenantModel.FindUnisolatedEntityTypes"/> lists the types this checks.
    /// </remarks>
    public UnclassifiedEntityTypeBehavior OnUnclassifiedEntityType { get; set; } = UnclassifiedEntityTypeBehavior.Reject;

    internal EfCoreIsolationOptions Clone() => (EfCoreIsolationOptions)MemberwiseClone();
}
