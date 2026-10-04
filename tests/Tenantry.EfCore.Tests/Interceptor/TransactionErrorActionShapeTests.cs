using System.Data.Common;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Canaries for the names EF Core gives a failed transaction operation (<c>TransactionErrorEventData.Action</c>),
/// which EF Core does not document. <see cref="TenantTransactionInterceptor"/> treats only the names of operations
/// that leave nothing of a failed save as harmless; any other name refuses the commit after a save that relied on a
/// tenant check. When a new EF Core version fails here, check what the renamed operation leaves in the transaction
/// and update the names: until then such a failure refuses the commit, which is safe but stricter than needed.
/// </summary>
public sealed class TransactionErrorActionShapeTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();

    public void Dispose() => _connection.Dispose();

    public static TheoryData<Operation, bool> Operations()
    {
        TheoryData<Operation, bool> data = [];

        foreach (var operation in Enum.GetValues<Operation>())
        {
            data.Add(operation, false);
            data.Add(operation, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task AFailedOperation_HasTheNameOfThisEfCoreVersion(Operation operation, bool sync)
    {
        FailingTransactions failing = new(operation);
        await using PlainContext db = new(new DbContextOptionsBuilder<PlainContext>().UseSqlite(_connection).AddInterceptors(failing).Options);
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await FailAsync(transaction, operation, sync);

        failing.Actions.Should().Equal(operation switch
        {
            Operation.CreateSavepoint => TenantTransactionInterceptor.CreateSavepoint,
            Operation.RollbackToSavepoint => "RollbackToSavepoint",
            Operation.ReleaseSavepoint => TenantTransactionInterceptor.ReleaseSavepoint,
            Operation.Commit => TenantTransactionInterceptor.Commit,
            _ => TenantTransactionInterceptor.Rollback,
        });
    }

    private static async Task FailAsync(IDbContextTransaction transaction, Operation operation, bool sync)
    {
        if (operation is not Operation.CreateSavepoint)
        {
            await transaction.CreateSavepointAsync("s", TestContext.Current.CancellationToken);
        }

        Func<Task> act = (operation, sync) switch
        {
            (Operation.CreateSavepoint, true) => () => Task.Run(() => transaction.CreateSavepoint("s")),
            (Operation.CreateSavepoint, false) => () => transaction.CreateSavepointAsync("s"),
            (Operation.RollbackToSavepoint, true) => () => Task.Run(() => transaction.RollbackToSavepoint("s")),
            (Operation.RollbackToSavepoint, false) => () => transaction.RollbackToSavepointAsync("s"),
            (Operation.ReleaseSavepoint, true) => () => Task.Run(() => transaction.ReleaseSavepoint("s")),
            (Operation.ReleaseSavepoint, false) => () => transaction.ReleaseSavepointAsync("s"),
            (Operation.Commit, true) => () => Task.Run(transaction.Commit),
            (Operation.Commit, false) => () => transaction.CommitAsync(),
            (Operation.Rollback, true) => () => Task.Run(transaction.Rollback),
            _ => () => transaction.RollbackAsync(),
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("failed on purpose");
    }

    [Fact]
    public void OnlyARollbackToTheSavepoint_OrAnOperationOfAnotherName_CanLeaveASaveInTheTransaction()
    {
        TenantTransactionInterceptor.LeavesNoSave("RollbackToSavepoint").Should().BeFalse();
        TenantTransactionInterceptor.LeavesNoSave("SomeLaterOperation").Should().BeFalse();

        foreach (var harmless in new[]
                 {
                     TenantTransactionInterceptor.Commit, TenantTransactionInterceptor.Rollback,
                     TenantTransactionInterceptor.CreateSavepoint, TenantTransactionInterceptor.ReleaseSavepoint,
                 })
        {
            TenantTransactionInterceptor.LeavesNoSave(harmless).Should().BeTrue(harmless);
        }
    }

    public enum Operation
    {
        CreateSavepoint,
        RollbackToSavepoint,
        ReleaseSavepoint,
        Commit,
        Rollback,
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    // Fails one operation from its interceptor hook, which EF Core reports as that operation's failure.
    private sealed class FailingTransactions(Operation failing) : DbTransactionInterceptor
    {
        public List<string> Actions { get; } = [];

        public override InterceptionResult CreatingSavepoint(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            Fail(Operation.CreateSavepoint, result);

        public override ValueTask<InterceptionResult> CreatingSavepointAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail(Operation.CreateSavepoint, result));

        public override InterceptionResult RollingBackToSavepoint(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            Fail(Operation.RollbackToSavepoint, result);

        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail(Operation.RollbackToSavepoint, result));

        public override InterceptionResult ReleasingSavepoint(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            Fail(Operation.ReleaseSavepoint, result);

        public override ValueTask<InterceptionResult> ReleasingSavepointAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail(Operation.ReleaseSavepoint, result));

        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            Fail(Operation.Commit, result);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail(Operation.Commit, result));

        public override InterceptionResult TransactionRollingBack(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            Fail(Operation.Rollback, result);

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail(Operation.Rollback, result));

        public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Actions.Add(eventData.Action);

        public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Actions.Add(eventData.Action);
            return Task.CompletedTask;
        }

        private InterceptionResult Fail(Operation operation, InterceptionResult result) =>
            operation == failing ? throw new InvalidOperationException("failed on purpose") : result;
    }
}
