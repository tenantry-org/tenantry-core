namespace Tenantry.EfCore;

/// <summary>
/// What <c>SaveChanges</c> does, with <c>Database.AutoTransactionBehavior</c> set to <c>Never</c> and no transaction,
/// when the tenant check of some rows it writes is another of its statements: owned rows in a table of their own,
/// checked by their owner's statement, and the rows of an entity mapped to more than one table (table-per-type,
/// entity splitting), checked in the table with <c>TenantId</c>. Without a transaction, a statement EF Core sends
/// beside a check that fails would stay written. Set with <see cref="EfCoreIsolationOptions.OnSaveWithoutTransaction"/>.
/// </summary>
public enum SaveWithoutTransactionBehavior
{
    /// <summary>
    /// Run that save in a transaction EF Core begins and commits itself, as it does by default
    /// (<c>AutoTransactionBehavior.WhenNeeded</c>), and set <c>Never</c> back when the save ends. Other saves stay
    /// without one. The default.
    /// </summary>
    UseTransaction,

    /// <summary>
    /// Throw <see cref="TenantIsolationViolationException" />, of kind
    /// <see cref="TenantIsolationViolationKind.SaveWithoutTransaction"/>, before anything is sent. For a database or
    /// connection pooler that cannot run transactions.
    /// </summary>
    Reject,
}
