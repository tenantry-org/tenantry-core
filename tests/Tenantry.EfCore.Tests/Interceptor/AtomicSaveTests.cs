using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// Some rows a save writes carry no tenant check of their own: owned rows in a table of their own, which their owner's
/// statement checks, and an entity's rows outside the table with <c>TenantId</c> when it is mapped to more than one.
/// They are safe only if the save fails as a whole when that check fails. A stub with another tenant's key, attached
/// as the current tenant's, must change nothing, however the application has EF Core run the save: with an interceptor
/// that suppresses concurrency failures, without a transaction, or in a transaction of its own.
/// </summary>
/// <remarks>
/// SQLite runs a save's statements one at a time, independent ones by table name, and stops at the first that fails,
/// so the owned rows' table (CustomerPhones) and the derived table (AaDogs) sort before the checked one here.
/// </remarks>
public sealed class AtomicSaveTests : IDisposable
{
    private const string AcmeState = "acme|1:acme phone|acme detail";

    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    public static TheoryData<Forgery, bool> Forgeries()
    {
        TheoryData<Forgery, bool> data = [];

        foreach (var forgery in Enum.GetValues<Forgery>())
        {
            data.Add(forgery, false);
            data.Add(forgery, true);
        }

        return data;
    }

    public static TheoryData<Forgery, bool> ForgeriesBySuppressorPlace()
    {
        TheoryData<Forgery, bool> data = [];

        foreach (var forgery in Enum.GetValues<Forgery>())
        {
            data.Add(forgery, false);
            data.Add(forgery, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ForgeriesBySuppressorPlace))]
    public async Task AnInterceptorThatSuppressesConcurrencyFailures_CannotSuppressACheckOtherStatementsRelyOn(Forgery forgery, bool suppressorFirst)
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Suppressor = new SuppressConcurrencyFailures(), SuppressorFirst = suppressorFirst }))
        {
            Forge(db, forgery);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task AnInterceptorThatSuppressesConcurrencyFailures_StillSuppressesOneNothingElseReliesOn()
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Suppressor = new SuppressConcurrencyFailures() }))
        {
            Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
            db.Attach(stub);
            stub.Name = "from globex";

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task WithoutATransaction_AFailedCheckLeavesNothingWritten(Forgery forgery, bool sync)
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            Forge(db, forgery);

            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();
            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task WithoutATransaction_WhenSuchSavesAreRejected_NothingIsSent(Forgery forgery, bool sync)
    {
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Reject = true, Transactions = transactions }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            Forge(db, forgery);

            (await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.Kind.Should().Be(TenantIsolationViolationKind.SaveWithoutTransaction);
            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
        }

        transactions.Started.Should().Be(0);
        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task InATransactionWithoutAutomaticSavepoints_AFailedCheckIsRolledBack_AndTheTransactionStillCommits(Forgery forgery, bool sync)
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            db.Database.AutoSavepointsEnabled = false;
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, forgery);

            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            db.Database.AutoSavepointsEnabled.Should().BeFalse();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task InATransactionWithoutSavepoints_AFailedCheckStopsItsCommit(Forgery forgery, bool sync)
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true }))
        {
            var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, forgery);
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();

            var commit = sync ? (Func<Task>)(() => Task.Run(transaction.Commit)) : () => transaction.CommitAsync();
            var refused = (await commit.Should().ThrowAsync<TenantIsolationViolationException>()).Which;
            refused.Kind.Should().Be(TenantIsolationViolationKind.TransactionRolledBack);
            refused.Message.Should().Contain("rolled back, not committed").And.NotContain("acme");

            // The transaction is rolled back and gone, so the context can begin another.
            db.Database.CurrentTransaction.Should().BeNull();
            db.ChangeTracker.Clear();
            await using var next = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            db.Add(new Customer { Id = 2, Name = "globex" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await next.CommitAsync(TestContext.Current.CancellationToken);
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_AConflictOnARowNothingReliesOn_StillCommits()
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
            db.Attach(stub);
            stub.Name = "from globex";
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ASaveThatFailsBeforeItsCheckIsRead_StopsItsCommit()
    {
        // The forged phone's INSERT runs, then the second phone's fails on its key, so EF Core never reads the owner's
        // check: whether it held is unknown, so the commit is refused.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Id = 0, Number = "from globex" });
            stub.Phones.Add(new Phone { Id = 1, Number = "duplicate" });
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

            (await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .Which.Message.Should().Contain("failed, or did not end, after sending some of its statements");
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_AnInterceptorThatThrowsForTheConcurrencyFailure_StillStopsTheCommit()
    {
        // Registered first, it ends EF Core's loop over the interceptors before Tenantry's sees the failed check.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true, Before = new ThrowOwnConcurrencyException() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ASaveChangesFailedInterceptorThatThrows_DoesNotLoseTheRefusal()
    {
        // A new owner with acme's key: its INSERT fails, and an interceptor that translates the failure (as
        // EntityFramework.Exceptions does) keeps EF Core from raising the context's SaveChangesFailed event.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true, Before = new TranslateSaveFailures() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            db.Add(new Customer { Id = 1, Name = "globex", Phones = { new Phone { Id = 2, Number = "from globex" } } });
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("translated");

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ACommitWithACancelledToken_IsStillRolledBack()
    {
        await SeedAcmeAsync();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true }))
        {
            var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

            await transaction.Awaiting(t => t.CommitAsync(cancelled.Token)).Should().ThrowAsync<TenantIsolationViolationException>();
            db.Database.CurrentTransaction.Should().BeNull();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ASaveStoppedBeforeItSentAnything_DoesNotStopTheCommit()
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { NoSavepoints = true, Later = new StopFirstSave() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("stopped");

            // A query the context runs meanwhile is not the stopped save's.
            (await db.Set<Customer>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);

            db.ChangeTracker.Clear();
            db.Add(new Customer { Id = 2, Name = "second" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var check = await CreateAsync(_tenant.As("acme"));
        (await check.Set<Customer>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InATransactionWithoutSavepoints_ASaveNestedInASavedChangesInterceptor_DoesNotStopTheCommit(bool sync)
    {
        // An audit interceptor added before UseTenantry() saves a row of its own through the context from SavedChanges,
        // so its save runs inside the tenant's, which succeeded.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { NoSavepoints = true, Before = new SaveAgainWhenSaved() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await ChangeAsync(db, Change.AddOwnedRow);
            await SaveAsync(db, sync);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
        await using var check = await CreateAsync(_tenant.As("acme"));
        (await check.Set<Supplier>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task InATransactionWithSavepoints_ASaveCancelledBeforeItsCheck_StopsItsCommit()
    {
        // EF Core rolls back to its savepoint with the cancelled token, which fails, so the forged phone stays in the
        // transaction.
        await SeedAcmeAsync();
        using CancellationTokenSource cancellation = new();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Commands = new CancelOn("UPDATE \"Customers\"", cancellation) }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task WithoutATransaction_AChangeToTheTenantIdTableAlone_IsSavedWithoutOne_EvenWhenSuchSavesAreRejected()
    {
        // Its one UPDATE carries the TenantId token: no other statement relies on it.
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Reject = true, Transactions = transactions }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            (await db.Set<Dog>().SingleAsync(TestContext.Current.CancellationToken)).Name = "renamed";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        transactions.Started.Should().Be(0);
        await using var check = await CreateAsync(_tenant.As("acme"));
        (await check.Set<Dog>().SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be("renamed");
    }

    [Fact]
    public async Task ARefusedTransactionItsContextBegan_IsForgottenOnceEnded_WhenItsObjectIsHandedOn()
    {
        // Npgsql hands a transaction object out again for the next transaction on its connection: once the context
        // that began the refused transaction has ended it, a context the object is handed to must be able to commit.
        await SeedAcmeAsync();
        DbTransaction first;

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Transactions = new SavepointRollbackFails() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            first = transaction.GetDbTransaction();
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

            AtomicSave.Refusal(first).Should().NotBeNull();
        }

        AtomicSave.Used(first);

        AtomicSave.Refusal(first).Should().BeNull();
    }

    [Fact]
    public async Task ARefusedTransactionItsContextDidNotBegin_IsStillRefused_WhenHandedToAnother()
    {
        // A transaction handed to the context may outlive it, shared with another context that commits it.
        await SeedAcmeAsync();
        await using var shared = await _connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Transactions = new SavepointRollbackFails() }))
        {
            await db.Database.UseTransactionAsync(shared, TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        AtomicSave.Used(shared);

        AtomicSave.Refusal(shared).Should().NotBeNull();
        await shared.RollbackAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenEFCoreFailsToRollBackToItsSavepoint_AfterAFailedCheck_TheTransactionDoesNotCommit()
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Transactions = new SavepointRollbackFails() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    public static TheoryData<Change, bool> Changes()
    {
        TheoryData<Change, bool> data = [];

        foreach (var change in Enum.GetValues<Change>())
        {
            data.Add(change, false);
            data.Add(change, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task WithoutATransaction_TheTenantsOwnSaveWhoseRowsRelyOnAnother_RunsInOne_AndNeverIsKept(Change change, bool sync)
    {
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Transactions = transactions }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            await ChangeAsync(db, change);
            await SaveAsync(db, sync);

            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
        }

        transactions.Started.Should().Be(1);
        transactions.Committed.Should().Be(1);
        (await AcmeStateAsync()).Should().Be(change switch
        {
            Change.AddOwnedRow => "acme|1:acme phone,2:added|acme detail",
            Change.ChangeDerivedTable => "acme|1:acme phone|changed",
            Change.DeleteOwnerWithOwnedRows => "|<none>|acme detail",
            _ => "acme|1:acme phone|<none>",
        });
    }

    [Fact]
    public async Task WithoutATransaction_ASaveWhoseRowsEachCarryTheirCheck_BeginsNoTransaction()
    {
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Transactions = transactions }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            (await db.Set<Customer>().SingleAsync(TestContext.Current.CancellationToken)).Name = "renamed";
            db.Add(new Customer { Id = 2, Name = "second" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        transactions.Started.Should().Be(0);
    }

    [Fact]
    public async Task WithoutATransaction_ANewOwnerWithAKeyOfItsOwn_IsSavedInOne_AndOneWhoseKeyTheDatabaseGeneratesIsNot()
    {
        // Another tenant's row can have a key the application chooses, and its INSERT then fails: the owned rows'
        // statements must fail with it. No row has a key the database is yet to generate.
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using var db = await CreateAsync(_tenant.As("acme"), new Setup { Transactions = transactions });
        db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;

        db.Add(new Customer { Id = 5, Name = "new", Phones = { new Phone { Id = 1, Number = "new phone" } } });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        transactions.Started.Should().Be(1);

        db.Add(new Supplier { Contacts = { new Contact { Id = 1, Name = "contact" } } });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        transactions.Started.Should().Be(1);

        db.Add(new Dog { Id = 5, Detail = "new dog" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        transactions.Started.Should().Be(2);
        db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
    }

    [Fact]
    public async Task WithoutATransaction_AndARetryingExecutionStrategy_TheSaveRunsInEFCoresTransaction()
    {
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Transactions = transactions, Retrying = true }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        transactions.Started.Should().Be(1);
        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
    }

    [Fact]
    public async Task WhenALaterInterceptorStopsTheSave_TheNextSaveSetsNeverBack()
    {
        await SeedAcmeAsync();
        StopFirstSave stop = new();

        await using var db = await CreateAsync(_tenant.As("acme"), new Setup { Later = stop });
        db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        await ChangeAsync(db, Change.AddOwnedRow);

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("stopped");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
    }

    [Fact]
    public async Task AfterACancelledSave_NeverIsSetBack()
    {
        await SeedAcmeAsync();
        using CancellationTokenSource cancellation = new();

        await using var db = await CreateAsync(_tenant.As("acme"), new Setup { Commands = new CancelOnFirstCommand(cancellation) });
        db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        await ChangeAsync(db, Change.AddOwnedRow);

        await db.Awaiting(d => d.SaveChangesAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task APooledContext_StartsItsNextLeaseWithItsOwnSetting_AfterASaveALaterInterceptorStopped()
    {
        await SeedAcmeAsync();
        PooledDbContextFactory<NeverContext> pool = new(Options<NeverContext>(_tenant.As("acme"), new Setup { Later = new StopFirstSave() }));

        await using (var db = await pool.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.WhenNeeded, "the stopped save had no end");
        }

        await using (var db = await pool.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            (await db.Set<Customer>().SingleAsync(TestContext.Current.CancellationToken)).Name = "renamed";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.WhenNeeded, "the earlier lease's save is not this one's to end");
        }
    }

    public enum Forgery
    {
        StubOwnerAddsOwnedRow,
        StubOwnerChangesOwnedRow,
        StubOwnerDeletedWithOwnedRows,
        WrittenStubOwnerAddsOwnedRow,
        TablePerTypeDerivedTableUpdate,
        TablePerTypeDelete,
    }

    public enum Change
    {
        AddOwnedRow,
        ChangeDerivedTable,
        DeleteOwnerWithOwnedRows,
        DeleteTablePerTypeEntity,
    }

    private static Task SaveAsync(DbContext db, bool sync)
    {
        if (!sync)
        {
            return db.SaveChangesAsync();
        }

        db.SaveChanges();
        return Task.CompletedTask;
    }

    // Another tenant's (acme's) rows, through stubs with their keys attached as globex's.
    private static void Forge(DbContext db, Forgery forgery)
    {
        switch (forgery)
        {
            case Forgery.StubOwnerAddsOwnedRow:
                Customer adding = new() { Id = 1, TenantId = "globex", Name = "acme" };
                db.Attach(adding);
                adding.Phones.Add(new Phone { Id = 2, Number = "from globex" });
                break;
            case Forgery.StubOwnerChangesOwnedRow:
                Customer changing = new() { Id = 1, TenantId = "globex", Name = "acme", Phones = { new Phone { Id = 1, Number = "acme phone" } } };
                db.Attach(changing);
                changing.Phones[0].Number = "from globex";
                break;
            case Forgery.StubOwnerDeletedWithOwnedRows:
                db.Remove(new Customer { Id = 1, TenantId = "globex", Name = "acme", Phones = { new Phone { Id = 1, Number = "acme phone" } } });
                break;
            case Forgery.WrittenStubOwnerAddsOwnedRow:
                Customer written = new() { Id = 1, TenantId = "globex", Name = "acme" };
                db.Attach(written);
                written.Name = "from globex";
                written.Phones.Add(new Phone { Id = 2, Number = "from globex" });
                break;
            case Forgery.TablePerTypeDerivedTableUpdate:
                Dog dog = new() { Id = 1, TenantId = "globex", Detail = "acme detail" };
                db.Attach(dog);
                dog.Detail = "from globex";
                break;
            default:
                db.Remove(new Dog { Id = 1, TenantId = "globex", Detail = "acme detail" });
                break;
        }
    }

    private static async Task ChangeAsync(DbContext db, Change change)
    {
        switch (change)
        {
            case Change.AddOwnedRow:
                (await db.Set<Customer>().SingleAsync()).Phones.Add(new Phone { Id = 2, Number = "added" });
                break;
            case Change.ChangeDerivedTable:
                (await db.Set<Dog>().SingleAsync()).Detail = "changed";
                break;
            case Change.DeleteOwnerWithOwnedRows:
                db.Remove(await db.Set<Customer>().SingleAsync());
                break;
            default:
                db.Remove(await db.Set<Dog>().SingleAsync());
                break;
        }
    }

    private async Task SeedAcmeAsync()
    {
        await using var db = await CreateAsync(_tenant.As("acme"));
        db.Add(new Customer { Id = 1, Name = "acme", Phones = { new Phone { Id = 1, Number = "acme phone" } } });
        db.Add(new Dog { Id = 1, Detail = "acme detail" });
        await db.SaveChangesAsync();
    }

    // Acme's customer's name, its phones, and its dog's detail, as stored, whatever tenant they are stored under.
    private async Task<string> AcmeStateAsync()
    {
        await using var db = await CreateAsync(_tenant.As("acme"));
        var customer = await db.Set<Customer>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.Id == 1);
        var dog = await db.Set<Dog>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(d => d.Id == 1);
        var phones = customer is null ? "<none>" : string.Join(",", customer.Phones.OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.Number}"));
        return $"{customer?.Name}|{phones}|{dog?.Detail ?? "<none>"}";
    }

    private async Task<AtomicContext> CreateAsync(TestTenantContext tenant, Setup? setup = null)
    {
        AtomicContext db = new(Options<AtomicContext>(tenant, setup ?? new Setup()));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private DbContextOptions<TContext> Options<TContext>(TestTenantContext tenant, Setup setup)
        where TContext : DbContext
    {
        DbContextOptionsBuilder<TContext> builder = new();
        builder.UseSqlite(_connection, sqlite =>
        {
            if (setup.Retrying)
            {
                sqlite.ExecutionStrategy(dependencies => new RetryingStrategy(dependencies));
            }
        });

        if (setup is { Suppressor: { } first, SuppressorFirst: true })
        {
            builder.AddInterceptors(first);
        }

        if (setup.Before is { } before)
        {
            builder.AddInterceptors(before);
        }

        EfCoreIsolationOptions isolation = new()
        {
            OnSaveWithoutTransaction = setup.Reject ? SaveWithoutTransactionBehavior.Reject : SaveWithoutTransactionBehavior.UseTransaction,
        };
        builder.UseApplicationServiceProvider(DbContextFactory.Services(tenant, isolation)).UseTenantry();

        if (setup is { Suppressor: { } last, SuppressorFirst: false })
        {
            builder.AddInterceptors(last);
        }

        foreach (var interceptor in new IInterceptor?[] { setup.Later, setup.Transactions, setup.Commands })
        {
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }
        }

        if (setup.NoSavepoints)
        {
            builder.ReplaceService<IRelationalTransactionFactory, NoSavepointTransactionFactory>();
        }

        return builder.Options;
    }

    private sealed record Setup
    {
        public IInterceptor? Suppressor { get; init; }

        public bool SuppressorFirst { get; init; }

        public bool Reject { get; init; }

        public bool NoSavepoints { get; init; }

        public bool Retrying { get; init; }

        public IInterceptor? Before { get; init; }

        public IInterceptor? Later { get; init; }

        public DbTransactionInterceptor? Transactions { get; init; }

        public DbCommandInterceptor? Commands { get; init; }
    }

    // Suppresses every concurrency failure, as a "last write wins" or "already deleted" policy does.
    private sealed class SuppressConcurrencyFailures : SaveChangesInterceptor
    {
        public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    // Turns a concurrency failure into an exception of the application's own.
    private sealed class ThrowOwnConcurrencyException : SaveChangesInterceptor
    {
        public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result) =>
            throw new InvalidOperationException("the application's own");

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the application's own");
    }

    // Saves a row of its own through the context once a save of the context's has succeeded, as an audit log might.
    private sealed class SaveAgainWhenSaved : SaveChangesInterceptor
    {
        private bool _saving;

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                context.Add(new Supplier());
                context.SaveChanges();
                _saving = false;
            }

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                context.Add(new Supplier());
                await context.SaveChangesAsync(cancellationToken);
                _saving = false;
            }

            return result;
        }
    }

    // Translates a failed save into an exception of the application's own, as EntityFramework.Exceptions does.
    private sealed class TranslateSaveFailures : SaveChangesInterceptor
    {
        public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
            throw new InvalidOperationException("translated");

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("translated");
    }

    private class TransactionCounter : DbTransactionInterceptor
    {
        public int Started { get; private set; }

        public int Committed { get; private set; }

        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            Started++;
            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            Started++;
            return ValueTask.FromResult(result);
        }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed++;

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Committed++;
            return Task.CompletedTask;
        }
    }

    // A rollback to the save's savepoint that fails, which EF Core only logs.
    private sealed class SavepointRollbackFails : TransactionCounter
    {
        public override InterceptionResult RollingBackToSavepoint(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            throw new InvalidOperationException("rollback failed");

        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("rollback failed");
    }

    // A SavingChanges interceptor after Tenantry's that stops the first save, so it never begins or ends.
    private sealed class StopFirstSave : SaveChangesInterceptor
    {
        private bool _stopped;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            Stop() ? throw new InvalidOperationException("stopped") : result;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Stop() ? throw new InvalidOperationException("stopped") : ValueTask.FromResult(result);

        private bool Stop()
        {
            if (_stopped)
            {
                return false;
            }

            _stopped = true;
            return true;
        }
    }

    // Cancels the save as its first command is sent.
    private sealed class CancelOnFirstCommand(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    // Cancels the save as a command starting with the given text is sent.
    private sealed class CancelOn(string text, CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith(text, StringComparison.Ordinal))
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    // Transactions without savepoints, as SQL Server's with multiple active result sets.
    private sealed class NoSavepointTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        : RelationalTransactionFactory(dependencies)
    {
        public override RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned) =>
            new NoSavepointTransaction(connection, transaction, transactionId, logger, transactionOwned, Dependencies.SqlGenerationHelper);
    }

    private sealed class NoSavepointTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper)
        : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
        public override bool SupportsSavepoints => false;
    }

    public sealed class Customer : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;

        public List<Phone> Phones { get; } = [];
    }

    public sealed class Phone
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Number { get; set; } = string.Empty;
    }

    public sealed class Supplier : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Contact> Contacts { get; } = [];
    }

    public sealed class Contact
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
    }

    public class Animal : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class Dog : Animal
    {
        [MaxLength(64)]
        public string Detail { get; set; } = string.Empty;
    }

    public class AtomicContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(customer =>
            {
                customer.ToTable("Customers").Property(c => c.Id).ValueGeneratedNever();
                customer.OwnsMany(c => c.Phones, phone =>
                {
                    phone.ToTable("CustomerPhones");
                    phone.Property(p => p.Id).ValueGeneratedNever();
                });
            });
            modelBuilder.Entity<Supplier>().OwnsMany(s => s.Contacts, contact =>
            {
                contact.ToTable("SupplierContacts");
                contact.Property(c => c.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<Animal>(animal => animal.UseTptMappingStrategy().ToTable("Animals").Property(a => a.Id).ValueGeneratedNever());
            modelBuilder.Entity<Dog>().ToTable("AaDogs");
        }
    }

    // A pooled context whose own setting is to save without a transaction.
    public sealed class NeverContext : AtomicContext
    {
        public NeverContext(DbContextOptions<NeverContext> options)
            : base(options) =>
            Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }
}
