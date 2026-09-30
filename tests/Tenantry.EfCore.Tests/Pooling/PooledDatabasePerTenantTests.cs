using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Pooling;

public sealed class PooledNote : ITenantScoped<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>A pool-compatible context: its only constructor takes the options.</summary>
public sealed class PooledNotesContext(DbContextOptions<PooledNotesContext> options)
    : MultiTenantDbContext<string>(options)
{
    public DbSet<PooledNote> Notes => Set<PooledNote>();
}

/// <summary>
///     A pooled context reused across two tenant databases must only ever read and write the database of the
///     tenant that is current. EF Core keeps a pooled context's connection string between leases, so these
///     tests fail if any lease reuses the previous tenant's connection.
/// </summary>
public sealed class PooledDatabasePerTenantTests : IAsyncLifetime
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private readonly Dictionary<string, SqliteConnection> _databases = new()
    {
        ["acme"] = new SqliteConnection($"DataSource=pool-acme-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"),
        ["globex"] = new SqliteConnection($"DataSource=pool-globex-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
    };

    public async Task InitializeAsync()
    {
        foreach (var database in _databases.Values)
        {
            database.Open(); // keeps each shared in-memory database alive
        }

        await using var services = Build();

        foreach (var tenant in new[] { Acme, Globex })
        {
            await using var scope = services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(tenant);
            await scope.ServiceProvider.GetRequiredService<PooledNotesContext>().Database.EnsureCreatedAsync();
        }
    }

    public Task DisposeAsync()
    {
        foreach (var database in _databases.Values)
        {
            database.Dispose();
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ScopedContext_ReusedAcrossTenants_ReadsAndWritesOnlyTheCurrentTenantsDatabase()
    {
        await using var services = Build();
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();
        HashSet<Guid> instances = [];
        List<string> seen = [];

        foreach (var tenant in new[] { Acme, Globex, Acme })
        {
            await using var scope = scopes.CreateScope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<PooledNotesContext>();
            instances.Add(db.ContextId.InstanceId);

            db.Notes.Add(new PooledNote { Text = $"{tenant.TenantId} note" });
            await db.SaveChangesAsync();
            seen.Add($"{tenant.TenantId}:{await db.Notes.CountAsync()}");
        }

        instances.Should().ContainSingle("the one pooled instance serves every lease");
        seen.Should().Equal("acme:1", "globex:1", "acme:2");
        RowsIn("acme").Should().Equal("acme:acme note", "acme:acme note");
        RowsIn("globex").Should().Equal("globex:globex note");
    }

    [Fact]
    public async Task Factory_ConnectsEachLeaseToTheCurrentTenantsDatabase()
    {
        await using var services = Build();
        var factory = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>();
        var ambient = services.GetRequiredService<ITenantScope<string>>();

        using (ambient.BeginScope(Globex))
        {
            await using var db = factory.CreateDbContext();
            db.Notes.Add(new PooledNote { Text = "sync lease" });
            await db.SaveChangesAsync();
        }

        using (ambient.BeginScope(Acme))
        {
            await using var db = await factory.CreateDbContextAsync();
            db.Notes.Add(new PooledNote { Text = "async lease" });
            await db.SaveChangesAsync();
        }

        RowsIn("globex").Should().Equal("globex:sync lease");
        RowsIn("acme").Should().Equal("acme:async lease");
    }

    [Fact]
    public async Task ConcurrentLeases_EachWriteOnlyToTheirOwnTenantsDatabase()
    {
        await using var services = Build(poolSize: 2);
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(i => scopes.RunInScopeAsync(
            i % 2 == 0 ? "acme" : "globex",
            async (scope, ct) =>
            {
                var db = scope.ServiceProvider.GetRequiredService<PooledNotesContext>();
                db.Notes.Add(new PooledNote { Text = $"write {i}" });
                await Task.Yield();
                await db.SaveChangesAsync(ct);
            })));

        RowsIn("acme").Should().HaveCount(8).And.OnlyContain(row => row.StartsWith("acme:"));
        RowsIn("globex").Should().HaveCount(8).And.OnlyContain(row => row.StartsWith("globex:"));
    }

    [Fact]
    public async Task Leasing_WithoutACurrentTenant_Throws()
    {
        await using var services = Build();
        await using var scope = services.CreateAsyncScope();

        scope.ServiceProvider.Invoking(sp => sp.GetRequiredService<PooledNotesContext>())
            .Should().Throw<TenantNotResolvedException>();
    }

    [Fact]
    public async Task ContextUsedAfterTheTenantChanges_RefusesToOpenAConnection()
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();
        PooledNotesContext db;

        using (ambient.BeginScope(Acme))
        {
            db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
        }

        await using (db)
        using (ambient.BeginScope(Globex))
        {
            db.Notes.Add(new PooledNote { Text = "globex row in acme's database" });
            var act = () => db.SaveChangesAsync();

            (await act.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*");
        }

        RowsIn("acme").Should().BeEmpty();
        RowsIn("globex").Should().BeEmpty();
    }

    public static TheoryData<string, string> OpenConnectionCases()
    {
        TheoryData<string, string> cases = new();

        foreach (var openedBy in new[] { "OpenConnection", "BeginTransaction" })
            foreach (var command in new[] { "SaveChangesAsync", "SaveChanges", "CountAsync", "Count", "ExecuteSqlRaw", "ExecuteDelete" })
            {
                cases.Add(openedBy, command);
            }

        return cases;
    }

    // EF Core raises ConnectionOpening only when it opens a closed connection, so a connection opened while
    // Acme was current stays open, unchecked, when the same context is used as Globex. Every command must be
    // checked, or a Globex-stamped row lands in Acme's database.
    [Theory]
    [MemberData(nameof(OpenConnectionCases))]
    public async Task ContextWithAnOpenConnection_UsedAfterTheTenantChanges_RefusesToRunCommands(
        string openedBy,
        string command)
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();
        var factory = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>();
        PooledNotesContext db;

        using (ambient.BeginScope(Acme))
        {
            await using (var seed = factory.CreateDbContext())
            {
                seed.Notes.Add(new PooledNote { Text = "acme's own note" });
                await seed.SaveChangesAsync();
            }

            db = factory.CreateDbContext();

            if (openedBy == "BeginTransaction")
            {
                await db.Database.BeginTransactionAsync();
            }
            else
            {
                await db.Database.OpenConnectionAsync();
            }
        }

        await using (db)
        using (ambient.BeginScope(Globex))
        {
            db.Notes.Add(new PooledNote { Text = "globex row in acme's database" });

            Func<Task> act = command switch
            {
                "SaveChangesAsync" => () => db.SaveChangesAsync(),
                "SaveChanges" => () => Task.FromResult(db.SaveChanges()),
                "CountAsync" => () => db.Notes.IgnoreQueryFilters().CountAsync(),
                "Count" => () => Task.FromResult(db.Notes.IgnoreQueryFilters().Count()),
                "ExecuteSqlRaw" => () => db.Database.ExecuteSqlRawAsync("DELETE FROM Notes"),
                "ExecuteDelete" => () => db.Notes.IgnoreQueryFilters().ExecuteDeleteAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };

            (await act.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*");
        }

        RowsIn("acme").Should().Equal("acme:acme's own note");
        RowsIn("globex").Should().BeEmpty();
    }

    // The guard runs before Tenantry's own SaveChanges interceptor, so a rejected save does not stamp the wrong
    // tenant onto pending inserts; otherwise spoof detection would then reject the owning tenant's own save.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSave_LeavesPendingInsertsUnstamped_SoTheOwningTenantCanStillSave(bool openConnection)
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();

        using (ambient.BeginScope(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();

            if (openConnection)
            {
                await db.Database.OpenConnectionAsync();
            }

            var note = new PooledNote { Text = "added by acme" };
            db.Notes.Add(note);

            using (ambient.BeginScope(Globex))
            {
                await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
            }

            note.TenantId.Should().BeEmpty();
            await db.SaveChangesAsync();
        }

        RowsIn("acme").Should().Equal("acme:added by acme");
        RowsIn("globex").Should().BeEmpty();
    }

    // Deterministic: a context used outside its tenant fails at SaveChanges whether or not it has changes.
    [Fact]
    public async Task SaveChanges_WithNothingToSave_OutsideTheContextsTenant_Throws()
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();
        PooledNotesContext db;

        using (ambient.BeginScope(Acme))
        {
            db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
        }

        await using (db)
        {
            var act = () => db.SaveChangesAsync();

            (await act.Should().ThrowAsync<TenantIsolationViolationException>()).WithMessage("*current tenant is '(none)'*");
        }
    }

    [Theory]
    [InlineData("OpenConnection")]
    [InlineData("BeginTransaction")]
    public async Task ContextWithAnOpenConnection_UsedByItsOwnTenant_RunsCommands(string openedBy)
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();

        using (ambient.BeginScope(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
            var transaction = openedBy == "BeginTransaction" ? await db.Database.BeginTransactionAsync() : null;

            if (transaction is null)
            {
                await db.Database.OpenConnectionAsync();
            }

            db.Notes.Add(new PooledNote { Text = "inside an open connection" });
            await db.SaveChangesAsync();
            (await db.Notes.CountAsync()).Should().Be(1);

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }
        }

        RowsIn("acme").Should().Equal("acme:inside an open connection");
    }

    // The lease and tenant still match, so without comparing the connection itself Acme would read and write
    // Globex's database.
    [Theory]
    [InlineData("SetConnectionString")]
    [InlineData("SetDbConnection")]
    public async Task ContextWhoseConnectionTheAppReplaced_RefusesToRunCommands(string replacedBy)
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();

        using (ambient.BeginScope(Globex))
        {
            await using var seed = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
            seed.Notes.Add(new PooledNote { Text = "globex's own note" });
            await seed.SaveChangesAsync();
        }

        using (ambient.BeginScope(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();

            if (replacedBy == "SetConnectionString")
            {
                db.Database.SetConnectionString(_databases["globex"].ConnectionString);
            }
            else
            {
                db.Database.SetDbConnection(new SqliteConnection(_databases["globex"].ConnectionString), contextOwnsConnection: true);
            }

            db.Notes.Add(new PooledNote { Text = "acme row in globex's database" });

            (await db.Awaiting(d => d.Notes.IgnoreQueryFilters().CountAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*connection was changed*");
            (await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*connection was changed*");
        }

        RowsIn("globex").Should().Equal("globex:globex's own note");
        RowsIn("acme").Should().BeEmpty();
    }

    [Fact]
    public async Task ContextNotConnectedForItsCurrentLease_RefusesToOpenAConnection()
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantScope<string>>();

        using (ambient.BeginScope(Globex))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();

            // As if the context had been leased without the factory: its connection belongs to an earlier lease.
            TenantDatabaseLeases.Record(db, db.ContextId.Lease - 1, "globex");

            (await db.Awaiting(d => d.Notes.CountAsync()).Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*current lease*");
        }
    }

    [Fact]
    public async Task OnlyAnAsyncDelegate_WorksThroughCreateDbContextAsync()
    {
        await using var services = Build(asyncOnly: true);
        var factory = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>();

        using (services.GetRequiredService<ITenantScope<string>>().BeginScope(Acme))
        {
            await using (var db = await factory.CreateDbContextAsync())
            {
                (await db.Notes.CountAsync()).Should().Be(0);
            }

            var sync = () => factory.CreateDbContext();
            sync.Should().Throw<InvalidOperationException>().WithMessage("*ResolveAsync*");
        }
    }

    [Fact]
    public async Task WithoutConnectionStrings_FailsWithGuidance()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant => tenant.AddEfCoreIsolation());
        services.AddTenantDbContextPool<PooledNotesContext, string>((sp, options) =>
            options.UseSqlite().AddTenantInterceptors(sp));
        await using var provider = services.BuildServiceProvider();

        provider.Invoking(p => p.GetRequiredService<IDbContextFactory<PooledNotesContext>>())
            .Should().Throw<InvalidOperationException>().WithMessage("*UseConnectionStrings*");
    }

    private ServiceProvider Build(int poolSize = 1024, bool asyncOnly = false)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantryCore<string>(tenant =>
        {
            tenant.UseInMemoryStore([Acme, Globex]);
            tenant.UseConnectionStrings(options =>
            {
                if (asyncOnly)
                {
                    options.GetConnectionStringAsync = (t, _) => ValueTask.FromResult(_databases[t.TenantId].ConnectionString);
                }
                else
                {
                    options.GetConnectionString = t => _databases[t.TenantId].ConnectionString;
                }
            });
            tenant.AddEfCoreIsolation(options => options.DetectSpoofedWrites = true);
        });
        services.AddTenantDbContextPool<PooledNotesContext, string>(
            (sp, options) => options.UseSqlite().AddTenantInterceptors(sp),
            poolSize);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private List<string> RowsIn(string tenantId)
    {
        using var command = _databases[tenantId].CreateCommand();
        command.CommandText = "SELECT TenantId || ':' || Text FROM Notes ORDER BY Id";
        using var reader = command.ExecuteReader();
        List<string> rows = [];

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
