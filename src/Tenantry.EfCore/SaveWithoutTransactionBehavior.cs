namespace Tenantry.EfCore;

/// <summary>
/// What <c>SaveChanges</c> does when <c>Database.AutoTransactionBehavior</c> is <c>Never</c>, no transaction is open,
/// and some rows it writes are tenant-checked by another of its statements. Set with
/// <see cref="EfCoreIsolationOptions.OnSaveWithoutTransaction"/>.
/// </summary>
/// <remarks>
/// <para>
/// These rows are owned entities in a table of their own, checked by their owner's statement, entities mapped to more
/// than one table (table-per-type, entity splitting), checked in the table that has <c>TenantId</c>, and the join rows
/// of a many-to-many relationship, checked through the tenant-owned rows they join. Without a transaction, the other
/// statements stay written if that check fails.
/// </para>
/// <para>
/// With <see cref="UseTransaction"/>, other saves stay without a transaction. A transaction begun on the connection
/// through ADO.NET must be handed to EF Core with <c>Database.UseTransaction</c>, or EF Core cannot begin its own and
/// the save fails.
/// </para>
/// </remarks>
public enum SaveWithoutTransactionBehavior
{
    /// <summary>
    /// Run that save in a transaction EF Core begins and commits itself, as it does by default
    /// (<c>AutoTransactionBehavior.WhenNeeded</c>), and set <c>Never</c> back when the save ends. The default.
    /// </summary>
    UseTransaction,

    /// <summary>
    /// Throw <see cref="TenantIsolationViolationException" />, of kind
    /// <see cref="TenantIsolationViolationKind.SaveWithoutTransaction"/>, before anything is sent. For a database or
    /// connection pooler that cannot run transactions.
    /// </summary>
    Reject,
}
