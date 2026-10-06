using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// Keeps a save all-or-nothing when the tenant check of some of its rows is another of its statements, and refuses the
/// commit of a transaction EF Core cannot undo such a save in unless every save in it that sent a statement succeeded.
/// </summary>
/// <remarks>
/// <para>
/// Owned rows in a table of their own carry no tenant: their owner's <c>UPDATE</c> or <c>DELETE</c>, or its
/// <c>TenantId</c> written back with its concurrency token, checks it. So does the table with <c>TenantId</c> for an
/// entity mapped to more than one table, and a new owner's or such entity's <c>INSERT</c>, which fails on a key another
/// tenant's row has. A many-to-many join row is checked the same way through each tenant-owned row it joins. Those
/// other statements are safe only if the save fails as a whole when the check does:
/// </para>
/// <list type="bullet">
///   <item>The check's failure cannot be suppressed: EF Core lets an interceptor suppress a concurrency failure
///   (<c>ThrowingConcurrencyException</c>), and would then commit the rest, so the save interceptor throws it.</item>
///   <item>With <c>AutoTransactionBehavior.Never</c> and no transaction, EF Core runs the save in a transaction of its
///   own (<c>WhenNeeded</c>, set back when the save ends), or it is rejected
///   (<see cref="EfCoreIsolationOptions.OnSaveWithoutTransaction"/>).</item>
///   <item>In the application's transaction, EF Core rolls a failed save back to a savepoint it sets, so savepoints
///   are turned on for the save.</item>
///   <item>A transaction EF Core cannot undo a save in, one without savepoints (SQL Server with multiple active result
///   sets) or an ambient one (<c>TransactionScope</c>), is rolled back instead of committed when it is unsafe. An
///   ambient transaction is refused through a volatile enlistment that votes against it before the connection's own
///   (phase 0, so the connection's single-phase commit is kept).</item>
/// </list>
/// <para>
/// EF Core's save notices name the context but not the save, and saves of a context nest (one an interceptor runs from
/// another's <c>SavingChanges</c> or <c>SavedChanges</c>), so which save a notice is for is not always known. The
/// refusal therefore does not depend on it. Each such transaction keeps a ledger: how many saves sent a statement in
/// it, how many of those were confirmed, whether anything in it failed, and whether any save in it wrote rows whose
/// check is another of its statements. Once one did, the transaction is unsafe if anything failed or a save that sent
/// a statement was not confirmed. A save counts as sent at its first command, so one stopped before sending counts for
/// nothing. A failed or cancelled command, a failed check and a failed rollback to a savepoint (or any failed
/// transaction operation not known to leave nothing of a save) each mark the ledger failed directly, whatever another
/// interceptor does with the save's failure afterwards. Only the save interceptor's
/// <c>SavedChanges</c> confirms a save.
/// </para>
/// <para>
/// A confirmation must not be counted for the wrong save, as that could balance a failed one. Each context keeps the
/// saves that began and have not ended, the latest last, and a notice is taken for the latest. <c>SavedChanges</c>
/// confirms the latest only if it sent a statement and EF Core reports that the save wrote entities. A failure notice
/// ends the latest save, and, as the failed save may be any save still noted (one above it, stopped before it sent
/// anything, took the notice), counts every noted save that sent a statement as failed. Only a save Tenantry itself
/// rejected in <c>SavingChanges</c> is surely the latest, and ends alone. A failure notice that follows a confirmation
/// with nothing in between also takes that confirmation back: it is that save's second notice (an interceptor threw
/// from its <c>SavedChanges</c>), or another save's that a save in between was confirmed before (one a
/// <c>SaveChangesFailed</c> handler ran).
/// </para>
/// <para>
/// A failed save is therefore counted as failed whenever any notice of its failure reaches Tenantry, and every notice
/// does: Tenantry's save notices, command and transaction hooks are interceptors of EF Core's internal service
/// provider (<see cref="TenantSaveNoticeInterceptor"/>, <see cref="TenantTransactionInterceptor"/>), which EF Core runs
/// before every interceptor added with <c>AddInterceptors</c>, and any concurrency failure of a save that sent
/// statements marks the ledger failed when it is raised, before an interceptor can suppress or replace it or a
/// <c>SaveChangesFailed</c> handler subscribed before Tenantry's can throw. What still gets through: another options
/// extension that inserts an interceptor ahead of Tenantry's in the internal service provider, which can throw from a
/// notice before Tenantry's sees it; and a <see cref="DbUpdateConcurrencyException"/> thrown by application code from
/// a <c>SavedChanges</c> hook or handler, which EF Core reports only through the <c>SaveChangesFailed</c> event, after
/// every statement of the save ran and Tenantry confirmed it. That one is safe: the save's checks all held, and a
/// handler that keeps the notice from Tenantry only keeps the confirmation standing, which is then right. As Tenantry's
/// interceptors run first, a failed check is thrown, and a commit refused, before the application's interceptors and
/// <c>TransactionCommitting</c> hooks run, wherever they are registered.
/// </para>
/// <para>
/// A save sets back the settings it changed when it ends. A save that ended without Tenantry hearing of it (another
/// interceptor stopped or failed it first), or that a notice which may not have been its own took off the list, is set
/// back by a later save's end once nothing is left to save, as a save still running would then send nothing. Until
/// then its setting stays: a <c>SavingChanges</c> interceptor registered after Tenantry's that stops the save, or one
/// registered before it that turns the save's failure into another exception, leaves <c>AutoTransactionBehavior</c> at
/// <c>WhenNeeded</c> and <c>AutoSavepointsEnabled</c> at <c>true</c> until then. A new lease of a pooled context starts
/// afresh.
/// </para>
/// </remarks>
internal sealed class AtomicSave
{
    private static readonly IReadOnlySet<object> NoChecks = new HashSet<object>();

    // Each context's saves, in one lease of it.
    private static readonly ConditionalWeakTable<DbContext, Lease> Leases = [];

    // The ledger of each transaction EF Core cannot undo a save in: a DbTransaction without savepoints or an ambient
    // Transaction. Weak, and dropped when a DbTransaction begins again (Npgsql reuses its objects), commits or rolls back.
    private static readonly ConditionalWeakTable<object, Ledger> Ledgers = [];

    // The transaction each context last began through EF Core, which only that context ends.
    private static readonly ConditionalWeakTable<DbContext, DbTransaction> Begun = [];

    private readonly Ledger? _ledger;

    private ILogger _logger = NullLogger.Instance;
    private IReadOnlySet<object> _checks = NoChecks;
    private bool _restoreNever;
    private bool _restoreSavepointsOff;
    private bool _relies;
    private bool _sent;

    private AtomicSave(Ledger? ledger) => _ledger = ledger;

    // Whether it holds settings to set back or entities to check.
    private bool IsHeld => _restoreNever || _restoreSavepointsOff || _checks.Count > 0;

    /// <summary>Notes that a save of <paramref name="context"/> begins, in the transaction it has now.</summary>
    public static void Start(DbContext context)
    {
        if (Find(context) is not { } lease)
        {
            lease = new Lease(context.ContextId);
            Leases.AddOrUpdate(context, lease);

            // The only notice of a concurrency failure. A pooled context drops its handlers between leases.
            context.SaveChangesFailed -= OnFailed;
            context.SaveChangesFailed += OnFailed;
        }

        lease.Confirmed = null;
        lease.Saves.Add(new AtomicSave(Undoable(context)));
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
        if ((checks.Count == 0 && !insertsAreChecks) || Find(context) is not { Saves: [.., var save] })
        {
            return;
        }

        save._checks = checks;
        save._relies = true;
        save._logger = logger;
        save._ledger?.Rely(logger, context);
        var database = context.Database;

        if (!database.IsRelational() || save._ledger is not null)
        {
            return;
        }

        if (database.CurrentTransaction is not null)
        {
            if (!database.AutoSavepointsEnabled)
            {
                database.AutoSavepointsEnabled = true;
                save._restoreSavepointsOff = true;
            }
        }
        else if (database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
        {
            var contextName = context.GetType().Name;

            if (behavior == SaveWithoutTransactionBehavior.Reject)
            {
                throw new TenantIsolationViolationException(
                    TenantIsolationViolationKind.SaveWithoutTransaction,
                    contextName,
                    $"'{contextName}' is saving without a transaction (Database.AutoTransactionBehavior is Never) " +
                    "owned entities in a table of their own, entities mapped to more than one table, or the join " +
                    "rows of a many-to-many relationship, whose tenant another of the save's statements checks: if " +
                    "that check failed, the rest would stay written. Nothing was sent, as " +
                    "EfCoreIsolationOptions.OnSaveWithoutTransaction is Reject. Save them in a transaction, or set " +
                    "OnSaveWithoutTransaction to UseTransaction.");
            }

            // EF Core reads the setting once the SavingChanges interceptors have run, and then begins, commits or
            // rolls back the transaction itself, in its execution strategy.
            database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            save._restoreNever = true;
            TenantIsolationLog.SaveInTransaction(logger);
        }
    }

    /// <summary>Notes that a save of <paramref name="context"/> is sending a command in <paramref name="transaction"/>.</summary>
    public static void Sending(DbContext? context, DbTransaction? transaction)
    {
        if (context is null || Find(context) is not { } lease)
        {
            return;
        }

        lease.Confirmed = null;

        if (Find(context, transaction) is not { } ledger)
        {
            return;
        }

        var latest = lease.Saves.Count > 0 ? lease.Saves[^1] : null;

        if (latest is null || !ReferenceEquals(latest._ledger, ledger))
        {
            // Sent by a save the context has no note of, or in another transaction: nothing confirms it.
            ledger.Sent();
        }
        else if (!latest._sent)
        {
            latest._sent = true;
            ledger.Sent();
        }
    }

    /// <summary>Notes that a command a save of <paramref name="context"/> sent in <paramref name="transaction"/> failed or was cancelled.</summary>
    public static void CommandFailed(DbContext? context, DbTransaction? transaction)
    {
        if (context is not null && Find(context) is { } lease)
        {
            lease.Confirmed = null;
            Find(context, transaction)?.Fail(null);
        }
    }

    /// <summary>
    /// Notes that a save of <paramref name="context"/> failed on a write that matched no row. That write was sent in the
    /// transaction the context has now, so the transaction's ledger is marked failed, whether or not the write was a
    /// check and whatever an interceptor then makes of the failure.
    /// </summary>
    public static void ConcurrencyFailed(DbContext context)
    {
        if (Find(context) is not { } lease)
        {
            return;
        }

        lease.Confirmed = null;
        Find(context, context.Database.CurrentTransaction?.GetDbTransaction())?.Fail(null);
    }

    /// <summary>
    /// Whether any of <paramref name="failed"/>, whose write matched no row, checks the tenant of rows other statements
    /// of a save of <paramref name="context"/> write, so the failure must not be suppressed.
    /// </summary>
    public static bool IsCheck(DbContext context, IReadOnlyList<EntityEntry> failed)
    {
        if (Find(context) is not { } lease ||
            failed.FirstOrDefault(entry => lease.Saves.Concat(lease.Unsettled).Any(save => save._checks.Contains(entry.Entity))) is not { } check)
        {
            return false;
        }

        // Any save of the context's that is still noted: one that relied on the check is among them.
        lease.Confirmed = null;
        lease.FailedCheck = check.Entity.GetType().Name;
        Find(context, context.Database.CurrentTransaction?.GetDbTransaction())?.Fail(lease.FailedCheck);
        return true;
    }

    /// <summary>
    /// Confirms the latest save of <paramref name="context"/>, which succeeded, if it sent a command and
    /// <paramref name="entitiesSaved"/> shows that it wrote entities.
    /// </summary>
    public static void Saved(DbContext context, int entitiesSaved)
    {
        if (Find(context) is not { } lease)
        {
            return;
        }

        var save = End(context, lease, own: true);
        lease.Confirmed = save;

        if (save is { _sent: true, _ledger: { } ledger } && entitiesSaved > 0)
        {
            ledger.Confirm();
        }
    }

    /// <summary>
    /// Notes that the latest save of <paramref name="context"/> ended without succeeding: it failed, was cancelled, or
    /// Tenantry rejected it. Unless Tenantry rejected it, every save still noted that sent a statement counts as
    /// failed, and a notice right after a confirmation also takes that confirmation back.
    /// </summary>
    /// <param name="context">The context whose save ended.</param>
    /// <param name="failure">
    /// What the save interceptor's <c>SaveChangesFailed</c> was given, which the context's <c>SaveChangesFailed</c>
    /// event then reports again, or <see langword="null"/>.
    /// </param>
    /// <param name="rejected">
    /// Whether Tenantry's own <c>SavingChanges</c> stopped the save, so the notice is surely the latest save's.
    /// </param>
    public static void Failed(DbContext context, Exception? failure = null, bool rejected = false)
    {
        if (Find(context) is not { } lease)
        {
            return;
        }

        // Right after a confirmation, the notice is either that save's second (an interceptor threw from its
        // SavedChanges) or another's, which a save in between (one a SaveChangesFailed handler ran) was confirmed
        // before. Either way the confirmation may be wrong, and the latest save may still be running.
        if (lease.Confirmed is { _sent: true, _ledger: { } confirmed })
        {
            confirmed.Fail(null);
        }

        if (End(context, lease, own: lease.Confirmed is null) is { _sent: true, _ledger: { } ledger })
        {
            ledger.Fail(null);
        }

        // The failed save may be any of those still noted: a save above it that ended unheard, one an interceptor
        // stopped before it sent anything, takes the notice in its place.
        if (!rejected)
        {
            foreach (var other in lease.Saves)
            {
                if (other is { _sent: true, _ledger: { } listed })
                {
                    listed.Fail(null);
                }
            }
        }

        lease.Confirmed = null;

        if (failure is not null)
        {
            lease.Reported = failure;
        }
    }

    /// <summary>
    /// After EF Core failed to roll a failed save back to its savepoint in <paramref name="transaction"/>, or reported
    /// a failed operation it may have been: that save's statements may still be in it, so its commit is refused if a
    /// save of <paramref name="context"/> relied on a check.
    /// </summary>
    public static void SavepointNotRolledBack(DbContext? context, DbTransaction transaction)
    {
        if (context is not null && Find(context) is { } lease && lease.Saves.FindLast(save => save._relies) is { } relied)
        {
            var ledger = Open(context, transaction);
            ledger.Rely(relied._logger, context);
            ledger.Fail(lease.FailedCheck);
        }
    }

    /// <summary>Notes that <paramref name="context"/> began <paramref name="transaction"/>, a new one.</summary>
    public static void Began(DbContext? context, DbTransaction transaction)
    {
        Ledgers.Remove(transaction);

        if (context is not null)
        {
            Begun.AddOrUpdate(context, transaction);
        }
    }

    /// <summary>
    /// As <paramref name="transaction"/> is handed to a context: drops its ledger if the context that began it is gone,
    /// disposed or on a new lease, as Npgsql hands out its transaction objects again. The ledger of a transaction no
    /// context began, or whose context is still there, which may still be live, is kept.
    /// </summary>
    public static void Used(DbTransaction transaction)
    {
        if (Ledgers.TryGetValue(transaction, out var ledger) && ledger.Ended())
        {
            Ledgers.Remove(transaction);
        }
    }

    /// <summary>The violation that refuses <paramref name="transaction"/>'s commit, if it is unsafe, or <see langword="null"/>.</summary>
    public static TenantIsolationViolationException? Refusal(DbTransaction transaction) =>
        Ledgers.TryGetValue(transaction, out var ledger) && ledger.IsUnsafe
            ? ledger.Refuse("The transaction was rolled back, not committed")
            : null;

    /// <summary>Forgets the ledger of <paramref name="transaction"/>, which ended.</summary>
    public static void Forget(DbTransaction transaction) => Ledgers.Remove(transaction);

    private static Lease? Find(DbContext context) =>
        Leases.TryGetValue(context, out var lease) && lease.ContextId.Equals(context.ContextId) ? lease : null;

    // The ledger of the transaction a command of the context runs in: its DbTransaction, or else the ambient one.
    private static Ledger? Find(DbContext context, DbTransaction? transaction) =>
        ((object?)transaction ?? Ambient(context)) is { } key && Ledgers.TryGetValue(key, out var ledger) ? ledger : null;

    private static Transaction? Ambient(DbContext context) =>
        context.Database.IsRelational() ? context.Database.GetEnlistedTransaction() ?? Transaction.Current : null;

    // The ledger of the transaction the context has, if EF Core cannot undo a save in it.
    private static Ledger? Undoable(DbContext context)
    {
        if (!context.Database.IsRelational())
        {
            return null;
        }

        if (context.Database.CurrentTransaction is { } transaction)
        {
            return transaction.SupportsSavepoints ? null : Open(context, transaction.GetDbTransaction());
        }

        return Ambient(context) is { } ambient ? Open(context, ambient) : null;
    }

    private static Ledger Open(DbContext context, object transaction)
    {
        // Contexts on other threads may open the same transaction's ledger at once: one ledger, and one vote.
        var ledger = Ledgers.GetValue(transaction, _ => new Ledger(context.GetType().Name));

        if (transaction is Transaction ambient && ledger.ClaimVote())
        {
            ambient.EnlistVolatile(new Vote(ledger), EnlistmentOptions.EnlistDuringPrepareRequired);
        }

        if (transaction is DbTransaction own && Begun.TryGetValue(context, out var begun) && ReferenceEquals(begun, own))
        {
            ledger.BegunBy(context);
        }

        return ledger;
    }

    // Takes the latest save of the context off its list, as it ended. If the notice is surely its own, sets back what
    // it changed; if not, as it may still be running, keeps that for later. Once nothing is left to save, the saves
    // still noted, and those kept, are set back and let their entities go: any of them still running sends nothing.
    private static AtomicSave? End(DbContext context, Lease lease, bool own)
    {
        if (lease.Saves is not [.., var save])
        {
            return null;
        }

        lease.Saves.RemoveAt(lease.Saves.Count - 1);

        if (own)
        {
            save._checks = NoChecks;
            save.Restore(context);
        }
        else
        {
            lease.Unsettled.Add(save);
        }

        if ((lease.Saves.Exists(other => other.IsHeld) || lease.Unsettled.Count > 0) && !context.ChangeTracker.HasChanges())
        {
            foreach (var other in lease.Saves.Concat(lease.Unsettled))
            {
                other._checks = NoChecks;
                other.Restore(context);
            }

            lease.Unsettled.Clear();
        }

        return save;
    }

    // EF Core raises the event for a concurrency failure, which the interceptor is not told of, and after the
    // interceptor for any other, which is then already noted.
    private static void OnFailed(object? sender, SaveChangesFailedEventArgs e)
    {
        if (sender is not DbContext context || Find(context) is not { } lease)
        {
            return;
        }

        if (ReferenceEquals(lease.Reported, e.Exception))
        {
            lease.Reported = null;
            return;
        }

        Failed(context);
    }

    // Sets back what the save changed in the context's settings, only if nothing has set it since.
    private void Restore(DbContext context)
    {
        var database = context.Database;

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

    // A context's saves that began and are not known to have ended, the latest last, in one lease of it.
    private sealed class Lease(DbContextId contextId)
    {
        public DbContextId ContextId { get; } = contextId;

        public List<AtomicSave> Saves { get; } = [];

        // Saves a notice that may not have been theirs took off the list, until nothing is left to save.
        public List<AtomicSave> Unsettled { get; } = [];

        // The save just confirmed, until another notice or command: a failure notice then takes the confirmation back.
        public AtomicSave? Confirmed { get; set; }

        // The failure the save interceptor noted last, until the context's event reports it again.
        public Exception? Reported { get; set; }

        // The entity whose check failed last.
        public string? FailedCheck { get; set; }
    }

    // What the saves in one transaction EF Core cannot undo them in sent, and whether the transaction may commit.
    private sealed class Ledger(string contextName)
    {
        private int _sent;
        private int _confirmed;
        private bool _failed;
        private bool _relied;
        private string? _failedCheck;
        private string _contextName = contextName;
        private ILogger _logger = NullLogger.Instance;
        private WeakReference<DbContext>? _owner;
        private DbContextId _ownerId;
        private int _voted;

        // Unsafe once a save in it relied on another statement's check and anything failed or went unconfirmed.
        public bool IsUnsafe => Volatile.Read(ref _relied) &&
            (Volatile.Read(ref _failed) || Volatile.Read(ref _sent) != Volatile.Read(ref _confirmed));

        public void Sent() => Interlocked.Increment(ref _sent);

        // Whether the caller is the first to ask, and so enlists the transaction's vote.
        public bool ClaimVote() => Interlocked.Exchange(ref _voted, 1) == 0;

        public void Confirm() => Interlocked.Increment(ref _confirmed);

        public void Fail(string? check)
        {
            _failedCheck ??= check;
            Volatile.Write(ref _failed, true);
        }

        public void Rely(ILogger logger, DbContext context)
        {
            _logger = logger;
            _contextName = context.GetType().Name;
            Volatile.Write(ref _relied, true);
        }

        public void BegunBy(DbContext context)
        {
            _owner = new WeakReference<DbContext>(context);
            _ownerId = context.ContextId;
        }

        // Whether the context that began the transaction is gone, disposed or on a new lease, so it ended the
        // transaction. A context that only let it go may have handed it on, still live.
        public bool Ended()
        {
            if (_owner is null)
            {
                return false;
            }

            if (!_owner.TryGetTarget(out var context) || !context.ContextId.Equals(_ownerId))
            {
                return true;
            }

            try
            {
                _ = context.Database;
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        public TenantIsolationViolationException Refuse(string outcome)
        {
            var typeName = _failedCheck ?? _contextName;
            TenantIsolationLog.TransactionNotCommitted(_logger, typeName);

            var failure = _failedCheck is null
                ? "a SaveChanges in it sent statements and did not succeed, or did not end"
                : $"a SaveChanges in it failed the tenant check of entity '{_failedCheck}'";

            return new TenantIsolationViolationException(
                TenantIsolationViolationKind.TransactionRolledBack,
                typeName,
                $"{outcome}: {failure}, and a SaveChanges in it wrote rows whose tenant another of its statements " +
                "checks. EF Core could not undo the failed save in this transaction (it has no savepoint, as with SQL " +
                "Server's multiple active result sets, or it is an ambient transaction), so rows it wrote could belong " +
                "to another tenant. Catching a failed SaveChanges (a unique key, a foreign key, a concurrency conflict) " +
                "and going on in the same transaction causes this too, as does a save in which an interceptor " +
                "suppressed a concurrency conflict (for a last-write-wins policy, for example). Run the unit of work " +
                "again in a new transaction, or use a transaction with savepoints, where EF Core undoes the failed save " +
                "itself: turn off multiple active result sets, or use Database.BeginTransaction rather than a " +
                "TransactionScope.");
        }
    }

    // Votes against completing an ambient transaction that is unsafe, as EF Core sets no savepoint in one.
    private sealed class Vote(Ledger ledger) : IEnlistmentNotification
    {
        public void Prepare(PreparingEnlistment preparingEnlistment)
        {
            if (ledger.IsUnsafe)
            {
                preparingEnlistment.ForceRollback(ledger.Refuse("The ambient transaction was rolled back"));
                return;
            }

            preparingEnlistment.Prepared();
        }

        public void Commit(Enlistment enlistment) => enlistment.Done();

        public void Rollback(Enlistment enlistment) => enlistment.Done();

        public void InDoubt(Enlistment enlistment) => enlistment.Done();
    }
}
