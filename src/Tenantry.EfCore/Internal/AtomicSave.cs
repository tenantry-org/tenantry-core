using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Keeps a save all-or-nothing when the tenant check of some of its rows is another of its statements, from
/// <c>SavingChanges</c> to the end of the save, and, in a transaction EF Core cannot undo the save in, to its commit.
/// </summary>
/// <remarks>
/// <para>
/// Owned rows in a table of their own carry no tenant: their owner's <c>UPDATE</c> or <c>DELETE</c>, or its
/// <c>TenantId</c> written back with its concurrency token, checks it. So does the table with <c>TenantId</c> for an
/// entity mapped to more than one table, and a new owner's or such entity's <c>INSERT</c>, which fails on a key another
/// tenant's row has. Those other statements are safe only if the save fails as a whole when the check does:
/// </para>
/// <list type="bullet">
///   <item>The check's failure cannot be suppressed: EF Core lets an interceptor suppress a concurrency failure
///   (<c>ThrowingConcurrencyException</c>), and would then commit the rest, so the save interceptor throws it.</item>
///   <item>With <c>AutoTransactionBehavior.Never</c> and no transaction, EF Core runs the save in a transaction of its
///   own (<c>WhenNeeded</c>, set back when the save ends), or it is rejected
///   (<see cref="EfCoreIsolationOptions.OnSaveWithoutTransaction"/>).</item>
///   <item>In the application's transaction, EF Core rolls a failed save back to a savepoint it sets, so savepoints
///   are turned on for the save. A transaction that has none (SQL Server with multiple active result sets), or that EF
///   Core failed to roll back to its savepoint, is rolled back instead of committed.</item>
///   <item>An ambient transaction (<c>TransactionScope</c>), which EF Core sets no savepoint in, is rolled back instead
///   of committed, through a volatile enlistment that votes against it.</item>
/// </list>
/// <para>
/// A failed save may have stopped before EF Core read the check, so whether it held is unknown: in a transaction EF
/// Core cannot undo, any failure of such a save after it sent a command refuses the commit, and so does a save whose
/// end Tenantry never saw. The state lives from the guard's check to the end of the save: the context's
/// <c>SavedChanges</c> and <c>SaveChangesFailed</c> events (the only notice of a concurrency failure) and the save
/// interceptor's <c>SavedChanges</c> and <c>SaveChangesCanceled</c> end it, and a save stopped before it began (a
/// later <c>SavingChanges</c> interceptor threw) is ended by the next save of the same lease of the context.
/// </para>
/// </remarks>
internal sealed class AtomicSave
{
    private static readonly ConditionalWeakTable<DbContext, AtomicSave> Saves = [];

    // The saves of each transaction EF Core cannot undo them in, whose commit is refused if one of them failed or
    // never ended. Weak, and cleared when the transaction begins again (Npgsql reuses its transaction objects) or rolls
    // back.
    private static readonly ConditionalWeakTable<DbTransaction, List<AtomicSave>> Undoable = [];

    private readonly DbContextId _contextId;
    private readonly IReadOnlySet<object> _checks;
    private readonly string _contextName;
    private readonly ILogger _logger;

    private bool _restoreNever;
    private bool _restoreSavepointsOff;
    private bool _sent;
    private Outcome _outcome;
    private string? _failedCheck;

    private AtomicSave(DbContext context, IReadOnlySet<object> checks, ILogger logger)
    {
        _contextId = context.ContextId;
        _checks = checks;
        _contextName = context.GetType().Name;
        _logger = logger;
    }

    private enum Outcome
    {
        Running,
        Saved,
        Failed,
    }

    // Whether the save leaves its transaction unsafe to commit: it failed, or never ended, after sending a command.
    private bool IsUnsafe => _outcome == Outcome.Failed || (_outcome == Outcome.Running && _sent);

    /// <summary>Ends what a save stopped before it began, or whose end went unseen, left behind.</summary>
    public static void Reset(DbContext context)
    {
        if (Saves.TryGetValue(context, out var save))
        {
            save.End(context, failed: true);
        }
    }

    /// <summary>
    /// Makes the save about to start all-or-nothing, when it writes rows whose tenant check is another of its
    /// statements.
    /// </summary>
    /// <param name="context">The saving context.</param>
    /// <param name="checks">
    /// The entities whose <c>UPDATE</c> or <c>DELETE</c> checks the tenant of rows other statements write.
    /// </param>
    /// <param name="insertsAreChecks">
    /// Whether a new entity's <c>INSERT</c> checks other statements' rows: they would join a row of another tenant's
    /// that has its key, if the save went on after that <c>INSERT</c> failed.
    /// </param>
    /// <param name="behavior">What to do without a transaction.</param>
    /// <param name="logger">The isolation's logger.</param>
    /// <exception cref="TenantIsolationViolationException">The save has no transaction, and <paramref name="behavior"/> rejects it.</exception>
    public static void Begin(
        DbContext context,
        IReadOnlySet<object> checks,
        bool insertsAreChecks,
        SaveWithoutTransactionBehavior behavior,
        ILogger logger)
    {
        if (checks.Count == 0 && !insertsAreChecks)
        {
            return;
        }

        AtomicSave save = new(context, checks, logger);
        var database = context.Database;

        if (database.IsRelational())
        {
            if (database.CurrentTransaction is { } transaction)
            {
                if (!transaction.SupportsSavepoints)
                {
                    Undoable.GetOrCreateValue(transaction.GetDbTransaction()).Add(save);
                }
                else if (!database.AutoSavepointsEnabled)
                {
                    database.AutoSavepointsEnabled = true;
                    save._restoreSavepointsOff = true;
                }
            }
            else if ((database.GetEnlistedTransaction() ?? Transaction.Current) is { } ambient)
            {
                ambient.EnlistVolatile(new Vote(save), EnlistmentOptions.None);
            }
            else if (database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
            {
                if (behavior == SaveWithoutTransactionBehavior.Reject)
                {
                    throw new TenantIsolationViolationException(
                        TenantIsolationViolationKind.SaveWithoutTransaction,
                        save._contextName,
                        $"'{save._contextName}' is saving without a transaction (Database.AutoTransactionBehavior is Never) " +
                        "owned entities in a table of their own, or entities mapped to more than one table, whose tenant " +
                        "another of the save's statements checks: if that check failed, the rest would stay written. " +
                        "Nothing was sent, as EfCoreIsolationOptions.OnSaveWithoutTransaction is Reject. Save them in a " +
                        "transaction, or set OnSaveWithoutTransaction to UseTransaction.");
                }

                // EF Core reads the setting once the SavingChanges interceptors have run, and then begins, commits or
                // rolls back the transaction itself, in its execution strategy.
                database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
                save._restoreNever = true;
                TenantIsolationLog.SaveInTransaction(logger);
            }
        }

        Saves.AddOrUpdate(context, save);
        context.SavedChanges += save.OnSaved;
        context.SaveChangesFailed += save.OnFailed;
    }

    /// <summary>Notes that the save of <paramref name="context"/> sent a command.</summary>
    public static void Sending(DbContext? context)
    {
        if (context is not null && Saves.TryGetValue(context, out var save) && save._contextId.Equals(context.ContextId))
        {
            save._sent = true;
        }
    }

    /// <summary>
    /// Whether any of <paramref name="failed"/>, whose write matched no row, checks the tenant of rows other statements
    /// of the save write, so the failure must not be suppressed.
    /// </summary>
    public static bool IsCheck(DbContext context, IReadOnlyList<EntityEntry> failed)
    {
        if (!Saves.TryGetValue(context, out var save) ||
            !save._contextId.Equals(context.ContextId) ||
            failed.FirstOrDefault(entry => save._checks.Contains(entry.Entity)) is not { } check)
        {
            return false;
        }

        save._failedCheck ??= check.Entity.GetType().Name;
        return true;
    }

    /// <summary>Ends the save of <paramref name="context"/>, which succeeded.</summary>
    public static void Saved(DbContext context)
    {
        if (Saves.TryGetValue(context, out var save))
        {
            save.End(context, failed: false);
        }
    }

    /// <summary>Ends the save <paramref name="context"/> cancelled.</summary>
    public static void Cancelled(DbContext context)
    {
        if (Saves.TryGetValue(context, out var save))
        {
            save.End(context, failed: true);
        }
    }

    /// <summary>
    /// After EF Core failed to roll a failed save back to its savepoint in <paramref name="transaction"/>: that save's
    /// statements are still in it, so its commit is refused.
    /// </summary>
    public static void SavepointNotRolledBack(DbContext? context, DbTransaction transaction)
    {
        if (context is not null && Saves.TryGetValue(context, out var save) && save._contextId.Equals(context.ContextId))
        {
            save._outcome = Outcome.Failed;
            Undoable.GetOrCreateValue(transaction).Add(save);
        }
    }

    /// <summary>
    /// The violation that refuses <paramref name="transaction"/>'s commit, if a save in it that EF Core could not undo
    /// failed or never ended, or <see langword="null"/>.
    /// </summary>
    public static TenantIsolationViolationException? Refusal(DbTransaction transaction) =>
        Undoable.TryGetValue(transaction, out var saves) && saves.FirstOrDefault(save => save.IsUnsafe) is { } unsafeSave
            ? unsafeSave.Refuse("The transaction was rolled back, not committed")
            : null;

    /// <summary>Forgets the saves of <paramref name="transaction"/>, which began again or rolled back.</summary>
    public static void Forget(DbTransaction transaction) => Undoable.Remove(transaction);

    private void OnSaved(object? sender, SavedChangesEventArgs e)
    {
        if (sender is DbContext context)
        {
            End(context, failed: false);
        }
    }

    private void OnFailed(object? sender, SaveChangesFailedEventArgs e)
    {
        if (sender is DbContext context)
        {
            End(context, failed: true);
        }
    }

    private void End(DbContext context, bool failed)
    {
        if (!Saves.TryGetValue(context, out var current) || current != this)
        {
            return;
        }

        Saves.Remove(context);
        context.SavedChanges -= OnSaved;
        context.SaveChangesFailed -= OnFailed;

        // A save that sent nothing changed nothing.
        if (_outcome == Outcome.Running)
        {
            _outcome = failed && _sent ? Outcome.Failed : Outcome.Saved;
        }

        // A later lease of a pooled context starts from the pool's settings.
        if (_contextId.Equals(context.ContextId))
        {
            Restore(context.Database);
        }
    }

    private void Restore(DatabaseFacade database)
    {
        // Only what Tenantry set, and only if nothing has set it since.
        if (_restoreNever && database.AutoTransactionBehavior == AutoTransactionBehavior.WhenNeeded)
        {
            database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        }

        if (_restoreSavepointsOff && database.AutoSavepointsEnabled)
        {
            database.AutoSavepointsEnabled = false;
        }
    }

    private TenantIsolationViolationException Refuse(string outcome)
    {
        var typeName = _failedCheck ?? _contextName;
        TenantIsolationLog.TransactionNotCommitted(_logger, typeName);

        var failure = _failedCheck is null
            ? "a SaveChanges in it failed, or did not end, after sending some of its statements"
            : $"a SaveChanges in it failed the tenant check of entity '{_failedCheck}'";

        return new TenantIsolationViolationException(
            TenantIsolationViolationKind.TransactionRolledBack,
            typeName,
            $"{outcome}: {failure}, and among them were rows whose tenant another of its statements checks. EF Core " +
            "could not undo that save in this transaction (it has no savepoint, as with SQL Server's multiple active " +
            "result sets, or it is an ambient transaction), so those rows could belong to another tenant. Change an " +
            "entity, or the entities it owns, only while its own tenant is current, and after any other failure run " +
            "the unit of work again.");
    }

    // Votes against committing an ambient transaction a save failed in, as EF Core sets no savepoint in one.
    private sealed class Vote(AtomicSave save) : IEnlistmentNotification
    {
        public void Prepare(PreparingEnlistment preparingEnlistment)
        {
            if (save.IsUnsafe)
            {
                preparingEnlistment.ForceRollback(save.Refuse("The ambient transaction was rolled back"));
                return;
            }

            preparingEnlistment.Prepared();
        }

        public void Commit(Enlistment enlistment) => enlistment.Done();

        public void Rollback(Enlistment enlistment) => enlistment.Done();

        public void InDoubt(Enlistment enlistment) => enlistment.Done();
    }
}
