using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tenantry.EfCore.Internal;

/// <summary>
/// EF Core interceptor that rolls back, rather than commits, a transaction <see cref="AtomicSave"/> finds unsafe, and
/// tells it which commands saves send in each transaction and which of them fail.
/// </summary>
/// <remarks>
/// A commit through the context's transaction (<c>Database.BeginTransaction</c> or <c>UseTransaction</c>) is refused:
/// the transaction is rolled back and disposed, whatever the commit's cancellation token, so the context can begin
/// another, and <see cref="TenantIsolationViolationException"/> is thrown. One committed through ADO.NET directly is
/// beyond reach. It holds no state of its own, so one instance serves every context.
/// </remarks>
internal sealed class TenantTransactionInterceptor : DbTransactionInterceptor, IDbCommandInterceptor
{
    public static readonly TenantTransactionInterceptor Instance = new();

    private const string RollbackToSavepoint = nameof(RollbackToSavepoint);

    private TenantTransactionInterceptor()
    {
    }

    /// <inheritdoc />
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        AtomicSave.Began(eventData.Context, result);
        return base.TransactionStarted(connection, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        AtomicSave.Began(eventData.Context, result);
        return base.TransactionStartedAsync(connection, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        AtomicSave.Used(result);
        return base.TransactionUsed(connection, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        AtomicSave.Used(result);
        return base.TransactionUsedAsync(connection, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        AtomicSave.Forget(transaction);
        base.TransactionCommitted(transaction, eventData);
    }

    /// <inheritdoc />
    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        AtomicSave.Forget(transaction);
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        if (AtomicSave.Refusal(transaction) is { } refusal)
        {
            if (Current(eventData.Context, transaction) is { } current)
            {
                current.Rollback();
                current.Dispose();
            }
            else
            {
                transaction.Rollback();
            }

            // Only once rolled back, so a commit that failed to roll back is refused again.
            AtomicSave.Forget(transaction);
            throw refusal;
        }

        return base.TransactionCommitting(transaction, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (AtomicSave.Refusal(transaction) is { } refusal)
        {
            // Not cancellable: a cancelled commit must not leave the transaction to commit later.
            if (Current(eventData.Context, transaction) is { } current)
            {
                await current.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                await current.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            AtomicSave.Forget(transaction);
            throw refusal;
        }

        return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        AtomicSave.Forget(transaction);
        base.TransactionRolledBack(transaction, eventData);
    }

    /// <inheritdoc />
    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        AtomicSave.Forget(transaction);
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        SavepointNotRolledBack(transaction, eventData);
        base.TransactionFailed(transaction, eventData);
    }

    /// <inheritdoc />
    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        SavepointNotRolledBack(transaction, eventData);
        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }

    // EF Core logs a failed rollback of a failed save to its savepoint and goes on, so that save's statements stay in
    // the transaction.
    private static void SavepointNotRolledBack(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        if (eventData.Action == RollbackToSavepoint)
        {
            AtomicSave.SavepointNotRolledBack(eventData.Context, transaction);
        }
    }

    // A save's commands, counted per transaction, and their failures, which make it unsafe whatever another
    // interceptor does with the save's failure (AtomicSave).
    public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Sending(command, eventData);
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Sending(command, eventData);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Sending(command, eventData);
        return result;
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Sending(command, eventData);
        return ValueTask.FromResult(result);
    }

    public void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Failed(command, eventData);

    public Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(command, eventData);
        return Task.CompletedTask;
    }

    public void CommandCanceled(DbCommand command, CommandEndEventData eventData) => Failed(command, eventData);

    public Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(command, eventData);
        return Task.CompletedTask;
    }

    // A save's own commands, not a query the context runs meanwhile.
    private static void Sending(DbCommand command, CommandEventData eventData)
    {
        if (eventData.CommandSource == CommandSource.SaveChanges)
        {
            AtomicSave.Sending(eventData.Context, command.Transaction);
        }
    }

    private static void Failed(DbCommand command, CommandEventData eventData)
    {
        if (eventData.CommandSource == CommandSource.SaveChanges)
        {
            AtomicSave.CommandFailed(eventData.Context, command.Transaction);
        }
    }

    // Disposing the context's transaction clears it, so the context can begin another.
    private static IDbContextTransaction? Current(DbContext? context, DbTransaction transaction) =>
        context?.Database.CurrentTransaction is { } current && ReferenceEquals(current.GetDbTransaction(), transaction)
            ? current
            : null;
}
