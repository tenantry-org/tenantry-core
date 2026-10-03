namespace Tenantry.EfCore;

/// <summary>
/// Options for EF Core tenant isolation: the application's, set with
/// <c>tenant.ConfigureEfCoreIsolation(options =&gt; …)</c>, or one context's, set with
/// <c>options.UseTenantry(o =&gt; …)</c>.
/// </summary>
/// <remarks>
/// These protections are <strong>always</strong> on, independent of these options: reads fail closed
/// (query filters match nothing when no tenant is resolved); a new entity that names another tenant is rejected;
/// <c>Modified</c>/<c>Deleted</c> entities must belong to the current tenant, checked before saving and again by
/// the stored tenant in each <c>UPDATE</c>/<c>DELETE</c>; and <c>ExecuteUpdate</c> cannot set <c>TenantId</c>.
/// These options govern writes without a tenant. Raw SQL and <c>IgnoreQueryFilters()</c> are outside Tenantry's
/// isolation.
/// </remarks>
public sealed class EfCoreIsolationOptions
{
    /// <summary>
    /// What happens when <c>SaveChanges</c> writes <see cref="ITenantEntity{TKey}" /> entities without a
    /// resolved tenant, or entities those own (EF Core owned types). Saves that write no tenant-owned entity are never
    /// affected.
    /// <list type="bullet">
    ///   <item><description><see cref="MissingTenantBehavior.Reject" /> — throw <see cref="TenantNotResolvedException" /> before persisting. <strong>Default.</strong></description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Warn" /> — allow the write and log a warning.</description></item>
    ///   <item><description><see cref="MissingTenantBehavior.Allow" /> — allow the write silently.</description></item>
    /// </list>
    /// <see cref="MissingTenantBehavior.Warn" /> and <see cref="MissingTenantBehavior.Allow" /> are for
    /// maintenance code that deliberately writes across tenants: updates and deletes are then not
    /// tenant-checked, and a new entity must set its <c>TenantId</c> explicitly, because an unowned row is
    /// always rejected. Reads always fail closed, whatever this setting.
    /// </summary>
    public MissingTenantBehavior OnMissingTenant { get; set; } = MissingTenantBehavior.Reject;

    /// <summary>
    /// What happens when, with <c>Database.AutoTransactionBehavior</c> set to <c>Never</c> and no transaction,
    /// <c>SaveChanges</c> writes rows whose tenant check is another of its statements: owned rows in a table of their own,
    /// or an entity mapped to more than one table.
    /// <list type="bullet">
    ///   <item><description><see cref="SaveWithoutTransactionBehavior.UseTransaction" /> — EF Core runs that save in a transaction of its own, as it does by default. <strong>Default.</strong></description></item>
    ///   <item><description><see cref="SaveWithoutTransactionBehavior.Reject" /> — throw <see cref="TenantIsolationViolationException" /> before anything is sent.</description></item>
    /// </list>
    /// Without a transaction, a statement sent beside a tenant check that fails would stay written. Saves whose rows
    /// each carry their own check are never affected, nor are saves with any other <c>AutoTransactionBehavior</c> or in
    /// a transaction. A transaction begun on the connection through ADO.NET must be handed to EF Core with
    /// <c>Database.UseTransaction</c>, or EF Core cannot begin its own and the save fails.
    /// </summary>
    public SaveWithoutTransactionBehavior OnSaveWithoutTransaction { get; set; } = SaveWithoutTransactionBehavior.UseTransaction;

    internal EfCoreIsolationOptions Clone() => (EfCoreIsolationOptions)MemberwiseClone();
}
