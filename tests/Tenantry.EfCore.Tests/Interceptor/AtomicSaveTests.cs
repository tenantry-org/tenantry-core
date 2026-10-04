using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Transactions;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InATransactionWithoutSavepoints_ALaterSaveWithNoChecks_DoesNotConfirmTheFailedOne(bool sync)
    {
        // The application catches the failed save, clears the tracker and saves a row no other statement checks.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();

            db.ChangeTracker.Clear();
            db.Add(new Supplier());
            await SaveAsync(db, sync);

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ALaterSaveWithoutATenant_DoesNotConfirmTheFailedOne()
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

            db.ChangeTracker.Clear();
            _tenant.AsNone();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ASaveThatFailsBeforeItsCheckIsRead_IsNotConfirmedByALaterSave()
    {
        // No concurrency failure is raised for the first save, and an interceptor that translates the failure keeps
        // EF Core from telling Tenantry that it failed.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true, Before = new TranslateSaveFailures() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Id = 0, Number = "from globex" });
            stub.Phones.Add(new Phone { Id = 1, Number = "duplicate" });
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("translated");

            db.ChangeTracker.Clear();
            db.Add(new Supplier());
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InATransactionWithoutSavepoints_AFailedSaveNestedInASaveWithNoChecks_IsNotConfirmedByIt(bool sync)
    {
        // An interceptor saves again from SavedChanges and swallows that save's failure. The outer save, which
        // succeeds, confirms itself only.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true, Before = new ForgeWhenSaved() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            db.Add(new Supplier());
            await SaveAsync(db, sync);

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InATransactionWithoutSavepoints_ASaveTenantryRejectsInsideAnother_EndsOnlyItself(bool sync)
    {
        // The nested save is rejected before it sends anything, and the interceptor swallows the rejection. The outer
        // save is still the one its own SavedChanges confirms.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { NoSavepoints = true, Before = new SaveForAnotherTenantWhenSaved() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await ChangeAsync(db, Change.AddOwnedRow);
            await SaveAsync(db, sync);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ASaveWithNoChecksThatFails_StopsTheCommitOnceALaterSaveReliesOnACheck()
    {
        // Which save a failure is for is not known, so any save that sent a statement and failed counts.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { NoSavepoints = true }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            db.Add(new Animal { Id = 1, Name = "duplicate key" });
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

            db.ChangeTracker.Clear();
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task InATransactionWithoutSavepoints_ASaveWithNoChecksThatFails_DoesNotStopTheCommit_WhenNoSaveReliesOnACheck()
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { NoSavepoints = true }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            db.Add(new Animal { Id = 1, Name = "duplicate key" });
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();

            db.ChangeTracker.Clear();
            db.Add(new Supplier());
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var check = await CreateAsync(_tenant.As("acme"));
        (await check.Set<Supplier>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InATransactionWithoutSavepoints_ASaveThatFailsWhileTheSaveAroundItRelies_StopsTheCommit_ThoughAnotherSaveRecordsTheFailure(bool sync)
    {
        // An interceptor saves again from SavedChanges, and that save fails on its key. An interceptor after Tenantry's
        // records the failure with a save of its own before EF Core raises the context's SaveChangesFailed event for
        // it. The failed save sent a statement, so the transaction is not committed.
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { NoSavepoints = true, Before = new FailAgainWhenSaved(), Later = new SaveWhenFailed() }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await ChangeAsync(db, Change.AddOwnedRow);
            await SaveAsync(db, sync);
            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    public static TheoryData<HiddenFailure, bool> HiddenFailures()
    {
        TheoryData<HiddenFailure, bool> data = [];

        foreach (var failure in Enum.GetValues<HiddenFailure>())
        {
            data.Add(failure, false);
            data.Add(failure, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HiddenFailures))]
    public async Task InATransactionWithoutSavepoints_AFailedSaveTenantryIsNotToldOf_IsNotConfirmedByALaterSaveThatSavesAgain(HiddenFailure failure, bool sync)
    {
        // An interceptor before Tenantry's hides the forged save's failure. A later save saves again from SavedChanges,
        // and an interceptor after Tenantry's throws from that nested save's SavedChanges, so EF Core reports the nested
        // save as saved and then as failed.
        await SeedAcmeAsync();
        SaveAgainWhenSaved again = new();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true, Earlier = new HideFirstFailure(), Before = again, Later = new ThrowWhenSaved(() => again.Saving) }))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await db.Awaiting(d => FailHiddenAsync(d, failure, sync)).Should().ThrowAsync<Exception>().Where(exception => exception is InvalidOperationException || exception is DbUpdateConcurrencyException);

            db.ChangeTracker.Clear();
            db.Add(new Supplier());
            await SaveAsync(db, sync);

            await transaction.Awaiting(t => t.CommitAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [MemberData(nameof(HiddenFailures))]
    public async Task InAnAmbientTransaction_AFailedSaveTenantryIsNotToldOf_IsNotConfirmedByALaterSaveThatSavesAgain(HiddenFailure failure, bool sync)
    {
        await SeedAcmeAsync();
        SaveAgainWhenSaved again = new();

        var act = async () =>
        {
            using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);

            await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Ambient = true, Earlier = new HideFirstFailure(), Before = again, Later = new ThrowWhenSaved(() => again.Saving) }))
            {
                await db.Awaiting(d => FailHiddenAsync(d, failure, sync)).Should().ThrowAsync<Exception>().Where(exception => exception is InvalidOperationException || exception is DbUpdateConcurrencyException);

                db.ChangeTracker.Clear();
                db.Add(new Supplier());
                await SaveAsync(db, sync);
            }

            scope.Complete();
        };

        (await act.Should().ThrowAsync<TransactionAbortedException>())
            .WithInnerException<TenantIsolationViolationException>();
    }

    public static TheoryData<Undoable, bool> UndoableTransactions()
    {
        TheoryData<Undoable, bool> data = [];

        foreach (var undoable in Enum.GetValues<Undoable>())
        {
            data.Add(undoable, false);
            data.Add(undoable, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task AFailedSaveTenantryIsNotToldOf_StillStopsTheCommit_AfterSavesThatSucceed(Undoable undoable, bool sync)
    {
        await SeedAcmeAsync();
        _tenant.As("globex");

        var committed = await CommitsAsync(undoable, new Setup { Earlier = new HideFirstFailure() }, async open =>
        {
            var db = await open();
            await db.Awaiting(d => FailHiddenAsync(d, HiddenFailure.Check, sync)).Should().ThrowAsync<Exception>().Where(exception => exception is InvalidOperationException || exception is DbUpdateConcurrencyException);

            db.ChangeTracker.Clear();
            db.Add(new Customer { Id = 2, Name = "globex", Phones = { new Phone { Id = 5, Number = "globex phone" } } });
            await SaveAsync(db, sync);
            db.Add(new Supplier());
            await SaveAsync(db, sync);
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task ASaveStoppedBeforeItSentAnything_DoesNotStopTheCommit(Undoable undoable, bool sync)
    {
        await SeedAcmeAsync();
        _tenant.As("acme");

        var committed = await CommitsAsync(undoable, new Setup { Later = new StopFirstSave() }, async open =>
        {
            var db = await open();
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<InvalidOperationException>().WithMessage("stopped");
            await SaveAsync(db, sync);
        });

        committed.Should().BeTrue();
        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task ASaveThatFailedBeforeItSentAnything_DoesNotStopTheCommit(Undoable undoable, bool sync)
    {
        await SeedAcmeAsync();
        _tenant.As("acme");

        var committed = await CommitsAsync(undoable, new Setup(), async open =>
        {
            var db = await open();
            var rejected = db.Add(new Supplier { TenantId = "globex" });
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>();

            rejected.State = EntityState.Detached;
            await ChangeAsync(db, Change.AddOwnedRow);
            await SaveAsync(db, sync);
        });

        committed.Should().BeTrue();
        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task ASaveThatFailsOnRowsASaveInsideItWroteWithoutAcceptingThem_StopsTheCommit(Undoable undoable, bool sync)
    {
        // The nested save writes the new owner and its owned row without accepting them, so the save around it sends
        // them again and fails on their keys.
        await SeedAcmeAsync();
        _tenant.As("acme");

        var committed = await CommitsAsync(undoable, new Setup { Later = new SaveAgainWhenSaving(acceptAllChanges: false) }, async open =>
        {
            var db = await open();
            db.Add(new Customer { Id = 5, Name = "new", Phones = { new Phone { Id = 7, Number = "new phone" } } });
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateException>();
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task ASaveInsideAnotherThatIsReportedSavedAndThenFailed_StopsTheCommit(Undoable undoable, bool sync)
    {
        // An interceptor after Tenantry's throws from the nested save's SavedChanges, once its rows are written, and
        // the interceptor that ran it swallows the failure. EF Core reported the save as failed, and a failure notice
        // right after a confirmation may be another save's, so the confirmation is taken back.
        await SeedAcmeAsync();
        _tenant.As("acme");
        SaveAgainWhenSaved again = new();

        var committed = await CommitsAsync(undoable, new Setup { Before = again, Later = new ThrowWhenSaved(() => again.Saving) }, async open =>
        {
            var db = await open();
            await ChangeAsync(db, Change.AddOwnedRow);
            await SaveAsync(db, sync);
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [InlineData(Undoable.WithoutSavepoints)]
    [InlineData(Undoable.Ambient)]
    public async Task ASaveCancelledAtItsCheck_StopsTheCommit(Undoable undoable)
    {
        await SeedAcmeAsync();
        _tenant.As("acme");
        using CancellationTokenSource cancellation = new();

        var committed = await CommitsAsync(undoable, new Setup { Commands = new CancelOn("UPDATE \"Customers\"", cancellation) }, async open =>
        {
            var db = await open();
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.Awaiting(d => d.SaveChangesAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task TwoContextsInOneTransaction_AFailedSaveOfEitherStopsTheCommit(Undoable undoable, bool sync)
    {
        await SeedAcmeAsync();
        _tenant.As("globex");

        var committed = await CommitsAsync(undoable, new Setup { Earlier = new HideFirstFailure() }, async open =>
        {
            var first = await open();
            var second = await open();
            second.Add(new Supplier());
            await SaveAsync(second, sync);

            await first.Awaiting(d => FailHiddenAsync(d, HiddenFailure.Check, sync)).Should().ThrowAsync<Exception>().Where(exception => exception is InvalidOperationException || exception is DbUpdateConcurrencyException);

            second.Add(new Supplier());
            await SaveAsync(second, sync);
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task TwoContextsInOneTransaction_WhoseSavesSucceed_Commit(Undoable undoable, bool sync)
    {
        await SeedAcmeAsync();
        _tenant.As("acme");

        var committed = await CommitsAsync(undoable, new Setup(), async open =>
        {
            var first = await open();
            var second = await open();
            await ChangeAsync(first, Change.AddOwnedRow);
            await SaveAsync(first, sync);
            await ChangeAsync(second, Change.ChangeDerivedTable);
            await SaveAsync(second, sync);
        });

        committed.Should().BeTrue();
        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|changed");
    }

    public static TheoryData<Undoable, SeenFailure> SeenFailures()
    {
        TheoryData<Undoable, SeenFailure> data = [];

        foreach (var undoable in Enum.GetValues<Undoable>())
        {
            foreach (var failure in Enum.GetValues<SeenFailure>())
            {
                data.Add(undoable, failure);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeenFailures))]
    public async Task AFailureTenantrySeesInASaveWhoseEndItIsNotToldOf_StopsTheCommit_ThoughThatSaveIsTakenForTheOneAroundIt(Undoable undoable, SeenFailure failure)
    {
        // An interceptor after Tenantry's saves again from SavingChanges. That save sends the forged owned row and fails,
        // and its failure notice is kept from Tenantry, so the save around it, which then saves a supplier, is taken for
        // it when it succeeds. Only the failure Tenantry saw (the failed INSERT, or the failed check) stops the commit.
        await SeedAcmeAsync();
        _tenant.As("globex");
        var hidden = false;

        var committed = await CommitsAsync(undoable, new Setup { Earlier = failure == SeenFailure.Command ? new HideFirstFailure() : null, Later = new StartOverWhenSaving() }, async open =>
        {
            var db = await open();

            // Subscribed before Tenantry's first save does, so it runs first and keeps the event from Tenantry.
            db.SaveChangesFailed += (_, _) =>
            {
                if (failure == SeenFailure.Check && !hidden)
                {
                    hidden = true;
                    throw new InvalidOperationException("translated");
                }
            };

            Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
            db.Attach(stub);
            stub.Phones.Add(new Phone { Id = 0, Number = "from globex" });

            if (failure == SeenFailure.Command)
            {
                stub.Phones.Add(new Phone { Id = 1, Number = "duplicate" });
            }

            await db.SaveChangesAsync();
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task ASaveThatSentNothing_DoesNotConfirmAFailedSaveRunInsideIt(Undoable undoable, bool sync)
    {
        // A save with nothing to save succeeds, and an interceptor runs a forged save from its SavedChanges, whose
        // failure is kept from Tenantry. The save that sent nothing must not be taken to confirm it.
        await SeedAcmeAsync();
        _tenant.As("globex");

        var committed = await CommitsAsync(undoable, new Setup { Earlier = new HideFirstFailure(), Before = new ForgeWhenSaved() }, async open =>
        {
            var db = await open();
            await SaveAsync(db, sync);
        });

        committed.Should().BeFalse();
    }

    public static TheoryData<Undoable, Audit> Audits()
    {
        TheoryData<Undoable, Audit> data = [];

        foreach (var undoable in Enum.GetValues<Undoable>())
        {
            foreach (var audit in Enum.GetValues<Audit>())
            {
                data.Add(undoable, audit);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Audits))]
    public async Task AFailedSaveAnotherSaveRecords_StopsTheCommit_ThoughTheSaveAroundItIsTakenForIt(Undoable undoable, Audit audit)
    {
        // The forged owned row's INSERT runs, then a rating's UPDATE matches no row, before the owner's check. The
        // failure is recorded with a save of its own through the context, which succeeds, before Tenantry is told of
        // the failure. With the nesting, an interceptor after Tenantry's ran the failed save from SavingChanges and
        // swallowed its failure, and the save around it then saves a supplier.
        await SeedAcmeAsync();
        _tenant.As("globex");
        var setup = new Setup
        {
            Earlier = audit == Audit.FromAnInterceptorBeforeTenantrys ? new AuditWhenFailed() : null,
            Later = audit == Audit.WithoutNesting ? null : new StartOverWhenSaving(),
        };

        var committed = await CommitsAsync(undoable, setup, async open =>
        {
            var db = await open();

            if (audit != Audit.FromAnInterceptorBeforeTenantrys)
            {
                // Subscribed before Tenantry's first save of the context, so it runs before Tenantry hears of the
                // failure.
                db.SaveChangesFailed += (_, _) => AuditWhenFailed.Record(db);
            }

            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            Rating rating = new() { Id = 9, TenantId = "globex", Stars = 1 };
            db.Attach(rating);
            rating.Stars = 5;

            if (audit == Audit.WithoutNesting)
            {
                await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
            }
            else
            {
                await db.SaveChangesAsync();
            }
        });

        committed.Should().BeFalse();

        if (undoable == Undoable.WithoutSavepoints)
        {
            (await AcmeStateAsync()).Should().Be(AcmeState);
        }
    }

    // The sequences that once confirmed a failed save: its every notice was stopped by an interceptor registered before
    // UseTenantry(), or by a SaveChangesFailed handler subscribed before Tenantry's, which both throw.
    public enum Unheard
    {
        // A nested save's check fails; the interceptor throws for it, and from SaveChangesFailed, as EntityFramework.Exceptions does.
        FailedNoticeThrown,

        // As above, but the interceptor turns the failure into a cancellation, and throws from SaveChangesCanceled.
        CanceledNoticeThrown,

        // A save that sends nothing reports a result (SuppressWithResult), and the failed save runs from its SavedChanges.
        RunFromASuppressedSave,

        // A nested save fails on a row nothing relies on, in a transaction an earlier save relied on a check in.
        ConflictOnARowNothingReliesOn,
    }

    public static TheoryData<Unheard, Undoable, bool> UnheardFailures()
    {
        TheoryData<Unheard, Undoable, bool> data = [];

        foreach (var unheard in Enum.GetValues<Unheard>())
        {
            foreach (var undoable in Enum.GetValues<Undoable>())
            {
                data.Add(unheard, undoable, false);
                data.Add(unheard, undoable, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnheardFailures))]
    public async Task AFailedSave_WhoseNoticesAnApplicationsInterceptorOrHandlerStops_StillStopsTheCommit(Unheard unheard, Undoable undoable, bool sync)
    {
        await SeedAcmeAsync();
        NestWhenSaving nested = new(unheard == Unheard.ConflictOnARowNothingReliesOn ? ChangeAMissingCustomer : ForgeAnOwnedRow);
        var setup = unheard switch
        {
            Unheard.FailedNoticeThrown => new Setup { Earlier = new HideFirstFailure(), Later = nested },
            Unheard.CanceledNoticeThrown => new Setup { Earlier = new HideFirstFailureAsCancelled(), Later = nested },
            Unheard.RunFromASuppressedSave => new Setup { Earlier = new HideFirstFailure(), Before = new SuppressThenForgeWhenSaved() },
            _ => new Setup { Later = nested },
        };
        _tenant.As(unheard == Unheard.ConflictOnARowNothingReliesOn ? "acme" : "globex");

        var committed = await CommitsAsync(undoable, setup, async open =>
        {
            var db = await open();
            db.SaveChangesFailed += (_, _) => throw new InvalidOperationException("the application's handler");

            if (unheard == Unheard.ConflictOnARowNothingReliesOn)
            {
                await ChangeAsync(db, Change.AddOwnedRow);
                await SaveAsync(db, sync);
            }

            nested.Armed = true;
            db.Add(new Supplier());
            await SaveAsync(db, sync);
        });

        committed.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEntityAnInterceptorRegisteredBeforeUseTenantryAdds_IsStampedAndChecked(bool sync)
    {
        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Earlier = new AddWhenSaving(() => new Supplier()) }))
        {
            db.Add(new Customer { Id = 7, Name = "acme" });
            await SaveAsync(db, sync);
        }

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            (await db.Set<Supplier>().IgnoreQueryFilters().Select(s => s.TenantId).ToListAsync(TestContext.Current.CancellationToken))
                .Should().Equal("acme");
        }

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Earlier = new AddWhenSaving(() => new Supplier { TenantId = "globex" }) }))
        {
            db.Add(new Customer { Id = 8, Name = "acme" });
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<TenantIsolationViolationException>();
        }
    }

    [Fact]
    public async Task TenantrysNoticeAndTransactionInterceptors_RunFirst_AndOnce()
    {
        await using var db = await CreateAsync(_tenant.As("acme"), new Setup { Earlier = new HideFirstFailure() });
        var interceptors = db.GetService<IEnumerable<IInterceptor>>().ToList();

        interceptors.Take(2).Should().BeEquivalentTo(new IInterceptor[] { TenantSaveNoticeInterceptor.Instance, TenantTransactionInterceptor.Instance });
        interceptors.Count(i => i is TenantSaveNoticeInterceptor).Should().Be(1);
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors
            .Should().NotContain(i => i is TenantTransactionInterceptor || i is TenantSaveNoticeInterceptor)
            .And.ContainSingle(i => i is TenantSaveChangesInterceptor);
    }

    [Theory]
    [MemberData(nameof(UndoableTransactions))]
    public async Task AFailedSaveWhoseNoticeEndsAnAuditSaveThatWasStopped_StopsTheCommit(Undoable undoable, bool sync)
    {
        // An interceptor after Tenantry's runs the forged save from another save's SavingChanges and swallows its
        // failure: the owned row's INSERT runs, then a rating's UPDATE matches no row, before the owner's check. A
        // SaveChangesFailed handler tries to record the failure with a save of its own, which a validation interceptor
        // stops before it sends anything, so that save is still noted when Tenantry hears of the failure.
        await SeedAcmeAsync();
        _tenant.As("globex");
        StopWhile validation = new();

        var committed = await CommitsAsync(undoable, new Setup { Later = new StartOverWhenSaving(), Last = validation }, async open =>
        {
            var db = await open();
            db.SaveChangesFailed += (_, _) =>
            {
                try
                {
                    validation.Stopping = true;
                    db.Add(new Supplier());
                    db.SaveChanges();
                }
                catch (InvalidOperationException)
                {
                    // The audit is best effort.
                }
                finally
                {
                    validation.Stopping = false;
                }
            };

            Forge(db, Forgery.StubOwnerAddsOwnedRow);
            Rating rating = new() { Id = 9, TenantId = "globex", Stars = 1 };
            db.Attach(rating);
            rating.Stars = 5;
            await SaveAsync(db, sync);
        });

        committed.Should().BeFalse();

        if (undoable == Undoable.WithoutSavepoints)
        {
            (await AcmeStateAsync()).Should().Be(AcmeState);
        }
    }

    [Fact]
    public void ContextsOpeningOneAmbientTransactionAtOnce_ShareOneLedger()
    {
        // The first context's save relies on a check and sends a statement that is never confirmed, so each scope must
        // be rolled back, whichever context opened the transaction's ledger.
        var options = Options<AtomicContext>(_tenant.As("acme"), new Setup { Ambient = true });
        var completed = 0;

        for (var i = 0; i < 1000; i++)
        {
            using AtomicContext first = new(options);
            using AtomicContext second = new(options);
            using Barrier barrier = new(2);

            try
            {
                using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);
                Thread other = new(() => StartAt(barrier, second));
                other.Start();
                StartAt(barrier, first);
                other.Join();

                AtomicSave.Guard(first, new HashSet<object>(), insertsAreChecks: true, SaveWithoutTransactionBehavior.UseTransaction, NullLogger.Instance);
                AtomicSave.Sending(first, null);
                scope.Complete();
            }
            catch (TransactionAbortedException)
            {
                continue;
            }

            completed++;
        }

        completed.Should().Be(0);
    }

    [Fact]
    public async Task ATransactionHandedOnAfterAFailedSave_IsStillRefused()
    {
        await SeedAcmeAsync();

        await using var first = await CreateAsync(_tenant.As("globex"), new Setup { NoSavepoints = true });
        await using var second = await CreateAsync(_tenant, new Setup { NoSavepoints = true });
        var transaction = (await first.Database.BeginTransactionAsync(TestContext.Current.CancellationToken)).GetDbTransaction();
        Forge(first, Forgery.StubOwnerAddsOwnedRow);
        await first.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();

        await first.Database.UseTransactionAsync(null, TestContext.Current.CancellationToken);
        await second.Database.UseTransactionAsync(transaction, TestContext.Current.CancellationToken);

        await second.Awaiting(d => d.Database.CommitTransactionAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Fact]
    public async Task ASaveStoppedBeforeItSentAnything_LetsItsEntitiesGo_OnceALaterSaveLeavesNothingToSave()
    {
        await SeedAcmeAsync();

        await using var db = await CreateAsync(_tenant.As("globex"), new Setup { Later = new StopFirstSave() });
        var stub = await StopForgedSaveAsync(db);
        db.ChangeTracker.Clear();
        db.Add(new Supplier());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        stub.TryGetTarget(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(Undoable.WithoutSavepoints)]
    [InlineData(Undoable.Ambient)]
    public async Task APooledContext_StartsEachLeaseAfresh_AfterAFailedSaveTenantryWasNotToldOf(Undoable undoable)
    {
        // The first lease leaves its failed save noted and unconfirmed; the next lease of the same context saves and
        // commits in a transaction of its own.
        await SeedAcmeAsync();
        var setup = undoable == Undoable.Ambient ? new Setup { Ambient = true, Earlier = new HideFirstFailure() } : new Setup { NoSavepoints = true, Earlier = new HideFirstFailure() };
        PooledDbContextFactory<AtomicContext> pool = new(Options<AtomicContext>(_tenant, setup));
        AtomicContext? first = null;

        _tenant.As("globex");
        (await CommitsAsync(undoable, pool, async db =>
        {
            first = db;
            await db.Awaiting(d => FailHiddenAsync(d, HiddenFailure.Check, sync: false)).Should().ThrowAsync<Exception>().Where(exception => exception is InvalidOperationException || exception is DbUpdateConcurrencyException);
        })).Should().BeFalse();

        _tenant.As("acme");
        (await CommitsAsync(undoable, pool, async db =>
        {
            db.Should().BeSameAs(first);
            await ChangeAsync(db, Change.AddOwnedRow);
            await db.SaveChangesAsync();
        })).Should().BeTrue();

        if (undoable == Undoable.WithoutSavepoints)
        {
            // SQLite does not enlist in an ambient transaction, so there the forged row stays whatever the vote.
            (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutATransaction_ASaveInsideTheSavingChangesOfAnInterceptorAfterTenantrys_LeavesTheSaveAroundItItsTransaction(bool sync)
    {
        // The nested save sends the forged rows and fails, and the interceptor swallows the failure. The save around it
        // sends them again, in the transaction Tenantry turned on for it.
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Later = new SaveAgainWhenSaving(), Transactions = transactions }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            Forge(db, Forgery.StubOwnerAddsOwnedRow);

            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();
            db.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Never);
        }

        transactions.Started.Should().Be(2);
        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutATransaction_ASaveInsideAnotherReportedSavedAndThenFailed_LeavesTheSaveAroundItItsTransaction(bool sync)
    {
        // The nested save writes the new owner and its owned row without accepting them, and is then reported failed.
        // That notice may be the save around it's, but that save is still to send the rows again, in its transaction.
        await SeedAcmeAsync();
        TransactionCounter transactions = new();

        await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Later = new SaveAgainWhenSaving(acceptAllChanges: false, throwWhenSaved: true), Transactions = transactions }))
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            db.Add(new Customer { Id = 5, Name = "new", Phones = { new Phone { Id = 7, Number = "new phone" } } });
            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateException>();
        }

        transactions.Started.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InATransactionWithoutAutomaticSavepoints_ASaveInsideTheSavingChangesOfAnInterceptorAfterTenantrys_LeavesTheSaveAroundItItsSavepoint(bool sync)
    {
        await SeedAcmeAsync();

        await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Later = new SaveAgainWhenSaving() }))
        {
            db.Database.AutoSavepointsEnabled = false;
            await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Forge(db, Forgery.StubOwnerAddsOwnedRow);

            await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            db.Database.AutoSavepointsEnabled.Should().BeFalse();
        }

        (await AcmeStateAsync()).Should().Be(AcmeState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InAnAmbientTransaction_ALaterSaveWithNoChecks_DoesNotConfirmTheFailedOne(bool sync)
    {
        // SQLite does not enlist in the ambient transaction, so the failed save is undone here whatever the vote. The
        // vote is what a provider that does enlist relies on.
        await SeedAcmeAsync();

        var act = async () =>
        {
            using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);

            await using (var db = await CreateAsync(_tenant.As("globex"), new Setup { Ambient = true }))
            {
                Forge(db, Forgery.StubOwnerAddsOwnedRow);
                await db.Awaiting(d => SaveAsync(d, sync)).Should().ThrowAsync<DbUpdateConcurrencyException>();

                db.ChangeTracker.Clear();
                db.Add(new Supplier());
                await SaveAsync(db, sync);
            }

            scope.Complete();
        };

        (await act.Should().ThrowAsync<TransactionAbortedException>())
            .WithInnerException<TenantIsolationViolationException>();
    }

    [Fact]
    public async Task InAnAmbientTransaction_TheTenantsOwnSaves_Complete()
    {
        await SeedAcmeAsync();

        using (TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using (var db = await CreateAsync(_tenant.As("acme"), new Setup { Ambient = true }))
            {
                await ChangeAsync(db, Change.AddOwnedRow);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
                db.Add(new Supplier());
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            scope.Complete();
        }

        (await AcmeStateAsync()).Should().Be("acme|1:acme phone,2:added|acme detail");
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
                .Which.Message.Should().Contain("sent statements and did not succeed");
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

    // The transactions EF Core cannot undo a save in.
    public enum Undoable
    {
        WithoutSavepoints,
        Ambient,
    }

    // Where a failed save is recorded with a save of its own: a SaveChangesFailed handler added before Tenantry's, with
    // the failed save run inside another or not, or a SaveChangesFailed interceptor before Tenantry's.
    public enum Audit
    {
        FromAHandler,
        WithoutNesting,
        FromAnInterceptorBeforeTenantrys,
    }

    // What Tenantry sees of a failed save whose failure notice is kept from it: a failed command, or a failed check.
    public enum SeenFailure
    {
        Command,
        Check,
    }

    // How a forged save fails without Tenantry's interceptor being told: its owner's check matches no row, or a later
    // INSERT of the save fails on its key before EF Core reads that check.
    public enum HiddenFailure
    {
        Check,
        Command,
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

    // Runs the work in a transaction EF Core cannot undo a save in, with the contexts it opens through the function it is
    // given, and commits it: whether it committed, or was refused.
    private async Task<bool> CommitsAsync(Undoable undoable, Setup setup, Func<Func<Task<AtomicContext>>, Task> work)
    {
        setup = undoable == Undoable.Ambient ? setup with { Ambient = true } : setup with { NoSavepoints = true };
        List<AtomicContext> contexts = [];
        DbTransaction? transaction = null;

        async Task<AtomicContext> Open()
        {
            var db = await CreateAsync(_tenant, setup);
            contexts.Add(db);

            if (undoable == Undoable.WithoutSavepoints)
            {
                if (transaction is null)
                {
                    transaction = (await db.Database.BeginTransactionAsync()).GetDbTransaction();
                }
                else
                {
                    await db.Database.UseTransactionAsync(transaction);
                }
            }

            return db;
        }

        try
        {
            return await CommitsAsync(undoable, () => work(Open), () => contexts[0].Database.CommitTransactionAsync());
        }
        finally
        {
            foreach (var db in contexts)
            {
                await db.DisposeAsync();
            }
        }
    }

    // As above, with one context leased from the pool.
    private static async Task<bool> CommitsAsync(Undoable undoable, PooledDbContextFactory<AtomicContext> pool, Func<AtomicContext, Task> work)
    {
        AtomicContext? db = null;

        try
        {
            return await CommitsAsync(
                undoable,
                async () =>
                {
                    db = await pool.CreateDbContextAsync();

                    if (undoable == Undoable.WithoutSavepoints)
                    {
                        await db.Database.BeginTransactionAsync();
                    }

                    await work(db);
                },
                () => db!.Database.CommitTransactionAsync());
        }
        finally
        {
            if (db is not null)
            {
                await db.DisposeAsync();
            }
        }
    }

    private static async Task<bool> CommitsAsync(Undoable undoable, Func<Task> work, Func<Task> commit)
    {
        try
        {
            if (undoable == Undoable.Ambient)
            {
                using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);
                await work();
                scope.Complete();
            }
            else
            {
                await work();
                await commit();
            }

            return true;
        }
        catch (TransactionAbortedException aborted) when (aborted.InnerException is TenantIsolationViolationException)
        {
            return false;
        }
        catch (TenantIsolationViolationException refused) when (refused.Kind == TenantIsolationViolationKind.TransactionRolledBack)
        {
            return false;
        }
    }

    private static void StartAt(Barrier barrier, DbContext context)
    {
        barrier.SignalAndWait();
        AtomicSave.Start(context);
    }

    // A forged save that StopFirstSave stops after Tenantry's interceptor noted it: a weak reference to its stub.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<Customer>> StopForgedSaveAsync(DbContext db)
    {
        Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
        db.Attach(stub);
        stub.Phones.Add(new Phone { Id = 2, Number = "from globex" });
        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("stopped");
        return new WeakReference<Customer>(stub);
    }

    // A forged save that sends its owned row, then fails, with HideFirstFailure registered before Tenantry's.
    private static Task FailHiddenAsync(DbContext db, HiddenFailure failure, bool sync)
    {
        Customer stub = new() { Id = 1, TenantId = "globex", Name = "acme" };
        db.Attach(stub);
        stub.Phones.Add(new Phone { Id = 0, Number = "from globex" });

        if (failure == HiddenFailure.Command)
        {
            stub.Phones.Add(new Phone { Id = 1, Number = "duplicate" });
        }

        return SaveAsync(db, sync);
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

        foreach (var interceptor in new[] { setup.Earlier, setup.Before })
        {
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }
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

        foreach (var interceptor in new IInterceptor?[] { setup.Later, setup.Last, setup.Transactions, setup.Commands })
        {
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }
        }

        if (setup.Ambient)
        {
            builder.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.AmbientTransactionWarning));
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

        public bool Ambient { get; init; }

        public bool Retrying { get; init; }

        public IInterceptor? Earlier { get; init; }

        public IInterceptor? Before { get; init; }

        public IInterceptor? Later { get; init; }

        public IInterceptor? Last { get; init; }

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

    // Saves a row of its own through the context once the first save of the context's has succeeded, as an audit log
    // might, and swallows an exception another interceptor throws from that save's SavedChanges.
    private sealed class SaveAgainWhenSaved : SaveChangesInterceptor
    {
        private bool _done;

        public bool Saving { get; private set; }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;
                Saving = true;
                context.Add(new Supplier());

                try
                {
                    context.SaveChanges();
                }
                catch (InvalidOperationException)
                {
                    // ThrowWhenSaved threw once the row was saved.
                }
                finally
                {
                    Saving = false;
                }
            }

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;
                Saving = true;
                context.Add(new Supplier());

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // ThrowWhenSaved threw once the row was saved.
                }
                finally
                {
                    Saving = false;
                }
            }

            return result;
        }
    }

    // A validation interceptor after Tenantry's that stops a save in SavingChanges while told to.
    private sealed class StopWhile : SaveChangesInterceptor
    {
        public bool Stopping { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            Stopping ? throw new InvalidOperationException("not valid") : result;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Stopping ? throw new InvalidOperationException("not valid") : ValueTask.FromResult(result);
    }

    // Records a failed save with a supplier of its own, saved through the context.
    private sealed class AuditWhenFailed : SaveChangesInterceptor
    {
        public static void Record(DbContext context)
        {
            context.ChangeTracker.Clear();
            context.Add(new Supplier());
            context.SaveChanges();
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData) => Record(eventData.Context!);

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Record(eventData.Context!);
            return Task.CompletedTask;
        }
    }

    // Throws from SavedChanges while the condition holds, so EF Core reports that save as failed after it succeeded.
    private sealed class ThrowWhenSaved(Func<bool> condition) : SaveChangesInterceptor
    {
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
            condition() ? throw new InvalidOperationException("thrown when saved") : result;

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default) =>
            condition() ? throw new InvalidOperationException("thrown when saved") : ValueTask.FromResult(result);
    }

    // Keeps the first failed save from the interceptors after it: a concurrency failure becomes an exception of its own,
    // and the failure is then translated, as EntityFramework.Exceptions does.
    private sealed class HideFirstFailure : SaveChangesInterceptor
    {
        private bool _hidden;

        public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result) =>
            _hidden ? result : throw new InvalidOperationException("the application's own");

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            _hidden ? ValueTask.FromResult(result) : throw new InvalidOperationException("the application's own");

        public override void SaveChangesFailed(DbContextErrorEventData eventData) => Hide();

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Hide();
            return Task.CompletedTask;
        }

        private void Hide()
        {
            if (!_hidden)
            {
                _hidden = true;
                throw new InvalidOperationException("translated");
            }
        }
    }

    // Saves again through the context from the first save's SavingChanges, after Tenantry's interceptor, and swallows
    // that save's failure. It can throw from that save's SavedChanges, once its rows are written.
    private sealed class SaveAgainWhenSaving(bool acceptAllChanges = true, bool throwWhenSaved = false) : SaveChangesInterceptor
    {
        private bool _done;
        private bool _saving;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;
                _saving = true;

                try
                {
                    context.SaveChanges(acceptAllChanges);
                }
                catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
                {
                    // The save around this one sends the same rows again.
                }
                finally
                {
                    _saving = false;
                }
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;
                _saving = true;

                try
                {
                    await context.SaveChangesAsync(acceptAllChanges, cancellationToken);
                }
                catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
                {
                    // The save around this one sends the same rows again.
                }
                finally
                {
                    _saving = false;
                }
            }

            return result;
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
            throwWhenSaved && _saving ? throw new InvalidOperationException("thrown when saved") : result;

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default) =>
            throwWhenSaved && _saving ? throw new InvalidOperationException("thrown when saved") : ValueTask.FromResult(result);
    }

    // Saves again through the context from the first save's SavingChanges, after Tenantry's interceptor. When that
    // save fails, it starts over with a supplier of its own, which the save around it then saves.
    private sealed class StartOverWhenSaving : SaveChangesInterceptor
    {
        private bool _done;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;

                try
                {
                    context.SaveChanges();
                }
                catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
                {
                    StartOver(context);
                }
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
                {
                    StartOver(context);
                }
            }

            return result;
        }

        private static void StartOver(DbContext context)
        {
            context.ChangeTracker.Clear();
            context.Add(new Supplier());
        }
    }

    // Once a save of the context's has succeeded, saves a forged owned row through it and swallows the failure, or the
    // exception an interceptor translated it into.
    private sealed class ForgeWhenSaved : SaveChangesInterceptor
    {
        private bool _saving;

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                Forge(context, Forgery.StubOwnerAddsOwnedRow);

                try
                {
                    context.SaveChanges();
                }
                catch (Exception exception) when (exception is DbUpdateConcurrencyException or InvalidOperationException)
                {
                    context.ChangeTracker.Clear();
                }
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
                Forge(context, Forgery.StubOwnerAddsOwnedRow);

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is DbUpdateConcurrencyException or InvalidOperationException)
                {
                    context.ChangeTracker.Clear();
                }
            }

            return result;
        }
    }

    // Another tenant's (acme's) owned row through a stub of its owner, as a nested save's change.
    private static void ForgeAnOwnedRow(DbContext context) => Forge(context, Forgery.StubOwnerAddsOwnedRow);

    // An update of a customer no row has, which fails as a concurrency conflict on a row nothing relies on.
    private static void ChangeAMissingCustomer(DbContext context)
    {
        Customer missing = new() { Id = 99, TenantId = "acme", Name = "missing" };
        context.Attach(missing);
        missing.Name = "changed";
    }

    // Once armed, saves again from a save's SavingChanges, after Tenantry's interceptor, with a change of its own, and
    // swallows that save's failure. The save around it then saves a supplier of its own.
    private sealed class NestWhenSaving(Action<DbContext> change) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (Armed && eventData.Context is { } context)
            {
                Armed = false;
                change(context);

                try
                {
                    context.SaveChanges();
                }
                catch (Exception)
                {
                    StartOver(context);
                }
            }

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is { } context)
            {
                Armed = false;
                change(context);

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception)
                {
                    StartOver(context);
                }
            }

            return result;
        }

        private static void StartOver(DbContext context)
        {
            context.ChangeTracker.Clear();
            context.Add(new Supplier());
        }
    }

    // As HideFirstFailure, but the first failure becomes a cancellation, and its SaveChangesCanceled throws.
    private sealed class HideFirstFailureAsCancelled : SaveChangesInterceptor
    {
        private bool _hidden;

        public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result) =>
            _hidden ? result : throw new OperationCanceledException("the application's own");

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            _hidden ? ValueTask.FromResult(result) : throw new OperationCanceledException("the application's own");

        public override void SaveChangesCanceled(DbContextEventData eventData) => Hide();

        public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
        {
            Hide();
            return Task.CompletedTask;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData) => Hide();

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Hide();
            return Task.CompletedTask;
        }

        private void Hide()
        {
            if (!_hidden)
            {
                _hidden = true;
                throw new InvalidOperationException("translated");
            }
        }
    }

    // Stops the first save from sending anything but reports one entity saved (SuppressWithResult), and from that
    // save's SavedChanges saves a forged owned row and swallows its failure.
    private sealed class SuppressThenForgeWhenSaved : SaveChangesInterceptor
    {
        private bool _suppressed;
        private bool _forged;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            Suppress(result);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Suppress(result));

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (Forging(eventData) is { } context)
            {
                try
                {
                    context.SaveChanges();
                }
                catch (Exception)
                {
                    context.ChangeTracker.Clear();
                }
            }

            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Forging(eventData) is { } context)
            {
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception)
                {
                    context.ChangeTracker.Clear();
                }
            }

            return result;
        }

        private InterceptionResult<int> Suppress(InterceptionResult<int> result)
        {
            if (_suppressed)
            {
                return result;
            }

            _suppressed = true;
            return InterceptionResult<int>.SuppressWithResult(1);
        }

        private DbContext? Forging(SaveChangesCompletedEventData eventData)
        {
            if (_forged || eventData.Context is not { } context)
            {
                return null;
            }

            _forged = true;
            context.ChangeTracker.Clear();
            ForgeAnOwnedRow(context);
            return context;
        }
    }

    // Adds an entity of its own to the first save, from SavingChanges.
    private sealed class AddWhenSaving(Func<object> entity) : SaveChangesInterceptor
    {
        private bool _done;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Add(eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Add(eventData);
            return ValueTask.FromResult(result);
        }

        private void Add(DbContextEventData eventData)
        {
            if (!_done && eventData.Context is { } context)
            {
                _done = true;
                context.Add(entity());
            }
        }
    }

    // Once a save of the context's has succeeded, saves a row that names another tenant through it, which Tenantry
    // rejects, and swallows the rejection.
    private sealed class SaveForAnotherTenantWhenSaved : SaveChangesInterceptor
    {
        private bool _saving;

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                var entry = context.Add(new Supplier { TenantId = "globex" });

                try
                {
                    context.SaveChanges();
                }
                catch (TenantIsolationViolationException)
                {
                    entry.State = EntityState.Detached;
                }
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
                var entry = context.Add(new Supplier { TenantId = "globex" });

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (TenantIsolationViolationException)
                {
                    entry.State = EntityState.Detached;
                }
            }

            return result;
        }
    }

    // Once a save of the context's has succeeded, saves through it a row whose key is already taken, and swallows the
    // failure.
    private sealed class FailAgainWhenSaved : SaveChangesInterceptor
    {
        private bool _saving;

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                context.Add(new Animal { Id = 1, Name = "duplicate key" });

                try
                {
                    context.SaveChanges();
                }
                catch (DbUpdateException)
                {
                    // SaveWhenFailed has let the row go.
                }
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
                context.Add(new Animal { Id = 1, Name = "duplicate key" });

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // SaveWhenFailed has let the row go.
                }
            }

            return result;
        }
    }

    // Records a failed save with a row of its own, saved through the context, once the failed rows are let go.
    private sealed class SaveWhenFailed : SaveChangesInterceptor
    {
        private bool _saving;

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                Record(context, eventData.Exception);
                context.SaveChanges();
            }
        }

        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!_saving && eventData.Context is { } context)
            {
                _saving = true;
                Record(context, eventData.Exception);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        private static void Record(DbContext context, Exception failure)
        {
            foreach (var entry in ((DbUpdateException)failure).Entries)
            {
                entry.State = EntityState.Detached;
            }

            context.Add(new Supplier());
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

    // Its table sorts between the owned phones' and the customers', so SQLite runs its UPDATE after a new phone's
    // INSERT and before the owner's check.
    public sealed class Rating : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public int Stars { get; set; }
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
            modelBuilder.Entity<Rating>().ToTable("CustomerRatings").Property(r => r.Id).ValueGeneratedNever();
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
