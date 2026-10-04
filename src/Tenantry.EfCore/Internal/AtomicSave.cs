using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
///   of committed, through a volatile enlistment that votes against it before the connection's own (phase 0, so the
///   connection's single-phase commit is kept).</item>
/// </list>
/// <para>
/// A failed save may have stopped before EF Core read the check, so whether it held is unknown: in a transaction EF
/// Core cannot undo, a save that sent any of its commands and was not confirmed to succeed refuses the commit. Only the
/// save interceptor's <c>SavedChanges</c> confirms one, so a failure Tenantry does not hear of still refuses it.
/// </para>
/// <para>
/// A confirmation must be the save's own. A context's saves nest (a save in a <c>SavedChanges</c> interceptor runs
/// inside another), so each context keeps the saves that have begun and not ended in order, the latest last. Every
/// save is on that list from <c>SavingChanges</c>, whether or not any of its rows rely on another statement, and
/// leaves it when it ends: <c>SavedChanges</c> takes the latest off and confirms it, and a failure or cancellation
/// takes the latest off as failed, which nothing confirms afterwards. A save that ended without Tenantry hearing of
/// it (another interceptor threw first) stays on the list unconfirmed, beneath the saves that begin after it. A new
/// lease of a pooled context starts afresh.
/// </para>
/// </remarks>
internal sealed class AtomicSave
{
    private static readonly IReadOnlySet<object> NoChecks = new HashSet<object>();

    // Each context's saves that began and are not known to have ended, the latest last.
    private static readonly ConditionalWeakTable<DbContext, Unconfirmed> Saves = [];

    // The saves of each transaction EF Core cannot undo them in. Weak, and cleared when the transaction begins again
    // (Npgsql reuses its transaction objects), commits or rolls back.
    private static readonly ConditionalWeakTable<DbTransaction, List<AtomicSave>> Undoable = [];

    // The transaction each context last began through EF Core, which only that context ends.
    private static readonly ConditionalWeakTable<DbContext, DbTransaction> Begun = [];

    private readonly WeakReference<DbContext> _context;
    private readonly DbContextId _contextId;
    private readonly string _contextName;

    private ILogger _logger = NullLogger.Instance;
    private IReadOnlySet<object> _checks = NoChecks;
    private bool _relied;
    private bool _restoreNever;
    private bool _restoreSavepointsOff;
    private bool _sent;
    private bool _saved;
    private bool _failed;
    private bool _inItsOwnTransaction;
    private string? _failedCheck;

    private AtomicSave(DbContext context)
    {
        _context = new WeakReference<DbContext>(context);
        _contextId = context.ContextId;
        _contextName = context.GetType().Name;
    }

    // Whether the save leaves its transaction unsafe to commit: it sent a command and failed, or was never confirmed.
    private bool IsUnsafe => _sent && (_failed || !_saved);

    /// <summary>
    /// Notes that a save of <paramref name="context"/> begins, and sets back what earlier saves that were never
    /// confirmed changed in the context's settings.
    /// </summary>
    /// <remarks>
    /// Every save is noted, so that the confirmation of one whose rows rely on nothing is not taken for an earlier
    /// save's.
    /// </remarks>
    public static void Start(DbContext context)
    {
        if (Find(context) is not { } unconfirmed)
        {
            unconfirmed = new Unconfirmed(context.ContextId);
            Saves.AddOrUpdate(context, unconfirmed);

            // The only notice of a concurrency failure. A pooled context drops its handlers between leases.
            context.SaveChangesFailed -= OnFailed;
            context.SaveChangesFailed += OnFailed;
        }

        foreach (var save in unconfirmed.Saves)
        {
            save.Finish(context);
        }

        unconfirmed.Saves.Add(new AtomicSave(context));
    }

    /// <summary>
    /// Makes the save of <paramref name="context"/> that <see cref="Start"/> noted all-or-nothing, when it writes rows
    /// whose tenant check is another of its statements.
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
    public static void Guard(
        DbContext context,
        IReadOnlySet<object> checks,
        bool insertsAreChecks,
        SaveWithoutTransactionBehavior behavior,
        ILogger logger)
    {
        if ((checks.Count == 0 && !insertsAreChecks) || Latest(context) is not { } save)
        {
            return;
        }

        save._checks = checks;
        save._logger = logger;
        save._relied = true;
        var database = context.Database;

        if (database.IsRelational())
        {
            if (database.CurrentTransaction is { } transaction)
            {
                if (!transaction.SupportsSavepoints)
                {
                    save.Register(context, transaction.GetDbTransaction());
                }
                else if (!database.AutoSavepointsEnabled)
                {
                    database.AutoSavepointsEnabled = true;
                    save._restoreSavepointsOff = true;
                }
            }
            else if ((database.GetEnlistedTransaction() ?? Transaction.Current) is { } ambient)
            {
                ambient.EnlistVolatile(new Vote(save), EnlistmentOptions.EnlistDuringPrepareRequired);
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
    }

    /// <summary>Notes that the latest save of <paramref name="context"/> sent one of its commands.</summary>
    public static void Sending(DbContext? context)
    {
        if (context is not null && Latest(context) is { } save)
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
        if (Latest(context) is not { } save || failed.FirstOrDefault(entry => save._checks.Contains(entry.Entity)) is not { } check)
        {
            return false;
        }

        // The save interceptor throws the failure, so the save fails whatever else hears of it.
        save._failedCheck ??= check.Entity.GetType().Name;
        save._failed = true;
        return true;
    }

    /// <summary>Confirms the latest save of <paramref name="context"/>, which succeeded.</summary>
    public static void Saved(DbContext context)
    {
        // One known to have failed, which no notice took off, ended inside the save that succeeded.
        while (End(context) is { } save)
        {
            if (!save._failed)
            {
                save._saved = true;
                return;
            }
        }
    }

    /// <summary>
    /// Notes that the latest save of <paramref name="context"/> ended without succeeding: it failed, was cancelled, or
    /// Tenantry rejected it. Nothing confirms it afterwards.
    /// </summary>
    /// <param name="context">The context whose save ended.</param>
    /// <param name="failure">
    /// What the save interceptor's <c>SaveChangesFailed</c> was given, which the context's <c>SaveChangesFailed</c>
    /// event then reports again, or <see langword="null"/>.
    /// </param>
    public static void Failed(DbContext context, Exception? failure = null)
    {
        if (End(context) is { } save)
        {
            save._failed = true;
        }

        if (failure is not null && Find(context) is { } unconfirmed)
        {
            unconfirmed.Reported = failure;
        }
    }

    /// <summary>
    /// After EF Core failed to roll a failed save back to its savepoint in <paramref name="transaction"/>: that save's
    /// statements are still in it, so its commit is refused.
    /// </summary>
    public static void SavepointNotRolledBack(DbContext? context, DbTransaction transaction)
    {
        if (context is not null && Latest(context) is { _relied: true } save)
        {
            save.Register(context, transaction);
        }
    }

    /// <summary>Notes that <paramref name="context"/> began <paramref name="transaction"/>, a new one.</summary>
    public static void Began(DbContext? context, DbTransaction transaction)
    {
        Undoable.Remove(transaction);

        if (context is not null)
        {
            Begun.AddOrUpdate(context, transaction);
        }
    }

    /// <summary>
    /// As <paramref name="transaction"/> is handed to a context: drops the saves of a transaction their context began
    /// and no longer has, which ended, as Npgsql hands out its transaction objects again. Saves of a transaction their
    /// context did not begin, which may still be live, are kept.
    /// </summary>
    public static void Used(DbTransaction transaction)
    {
        if (Undoable.TryGetValue(transaction, out var saves))
        {
            saves.RemoveAll(save => save._inItsOwnTransaction && !save.HasTransaction(transaction));
        }
    }

    /// <summary>
    /// The violation that refuses <paramref name="transaction"/>'s commit, if a save in it that EF Core could not undo
    /// sent a command and did not succeed, or <see langword="null"/>.
    /// </summary>
    public static TenantIsolationViolationException? Refusal(DbTransaction transaction) =>
        Undoable.TryGetValue(transaction, out var saves) && saves.FirstOrDefault(save => save.IsUnsafe) is { } unsafeSave
            ? unsafeSave.Refuse("The transaction was rolled back, not committed")
            : null;

    /// <summary>Forgets the saves of <paramref name="transaction"/>, which ended.</summary>
    public static void Forget(DbTransaction transaction) => Undoable.Remove(transaction);

    private static Unconfirmed? Find(DbContext context) =>
        Saves.TryGetValue(context, out var unconfirmed) && unconfirmed.ContextId.Equals(context.ContextId)
            ? unconfirmed
            : null;

    private static AtomicSave? Latest(DbContext context) =>
        Find(context) is { Saves.Count: > 0 } unconfirmed ? unconfirmed.Saves[^1] : null;

    // Takes the latest save of the context off its list, as it ended, and sets back what it changed.
    private static AtomicSave? End(DbContext context)
    {
        if (Find(context) is not { Saves.Count: > 0 } unconfirmed)
        {
            return null;
        }

        var save = unconfirmed.Saves[^1];
        unconfirmed.Saves.RemoveAt(unconfirmed.Saves.Count - 1);
        save.Finish(context);
        return save;
    }

    // EF Core raises the event for a concurrency failure, which the interceptor is not told of, and after the
    // interceptor for any other, which is then already noted.
    private static void OnFailed(object? sender, SaveChangesFailedEventArgs e)
    {
        if (sender is not DbContext context || Find(context) is not { } unconfirmed)
        {
            return;
        }

        if (ReferenceEquals(unconfirmed.Reported, e.Exception))
        {
            unconfirmed.Reported = null;
            return;
        }

        Failed(context);
    }

    private void Register(DbContext context, DbTransaction transaction)
    {
        var saves = Undoable.GetOrCreateValue(transaction);

        if (!saves.Contains(this))
        {
            saves.Add(this);
        }

        _inItsOwnTransaction = Begun.TryGetValue(context, out var begun) && ReferenceEquals(begun, transaction);
    }

    // Whether the save's context, in the same lease, still has the transaction.
    private bool HasTransaction(DbTransaction transaction)
    {
        if (!_context.TryGetTarget(out var context) || !context.ContextId.Equals(_contextId))
        {
            return false;
        }

        try
        {
            return context.Database.CurrentTransaction?.GetDbTransaction() is { } current && ReferenceEquals(current, transaction);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    // Sets back what the save changed in the context's settings, once it has ended, and lets its entities go.
    private void Finish(DbContext context)
    {
        _checks = NoChecks;

        // A later lease of a pooled context starts from the pool's settings.
        if (!_contextId.Equals(context.ContextId))
        {
            return;
        }

        var database = context.Database;

        // Only what Tenantry set, and only if nothing has set it since.
        if (_restoreNever && database.AutoTransactionBehavior == AutoTransactionBehavior.WhenNeeded)
        {
            database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        }

        if (_restoreSavepointsOff && database.AutoSavepointsEnabled)
        {
            database.AutoSavepointsEnabled = false;
        }

        _restoreNever = false;
        _restoreSavepointsOff = false;
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

    // A context's saves that began and are not known to have ended, in one lease of it.
    private sealed class Unconfirmed(DbContextId contextId)
    {
        public DbContextId ContextId { get; } = contextId;

        public List<AtomicSave> Saves { get; } = [];

        // The failure the save interceptor noted last, until the context's event reports it again.
        public Exception? Reported { get; set; }
    }

    // Votes against completing an ambient transaction a save failed in, as EF Core sets no savepoint in one.
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
