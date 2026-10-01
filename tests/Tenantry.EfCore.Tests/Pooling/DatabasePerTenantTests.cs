using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests.Pooling;

public sealed class PooledNote : ITenantEntity<string>
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>A pool-compatible context: its only constructor takes the options.</summary>
public sealed class PooledNotesContext(DbContextOptions<PooledNotesContext> options) : DbContext(options)
{
    public DbSet<PooledNote> Notes => Set<PooledNote>();
}

/// <summary>
///     <c>AddDbContextPerTenantDatabase</c> with a pool: a context reused across two tenant databases must only ever
///     read and write the database of the tenant that is current. EF Core keeps a pooled context's connection string
///     between leases, so these tests fail if any lease reuses the previous tenant's connection.
/// </summary>
public sealed class PooledDatabasePerTenantTests() : DatabasePerTenantTests(pooled: true);

/// <summary>
///     <c>AddDbContextPerTenantDatabase</c> without a pool: every context is new, and must still use only the
///     database of the tenant it was created for.
/// </summary>
public sealed class NonPooledDatabasePerTenantTests() : DatabasePerTenantTests(pooled: false)
{
    [Fact]
    public async Task Context_TakesOtherServicesInItsConstructor_FromItsScope()
    {
        await using var services = Build(configure: collection => collection.AddScoped<NotesSession>(), context: false);
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();

        await using var scope = scopes.CreateScope(Acme);
        var db = scope.ServiceProvider.GetRequiredService<SessionNotesContext>();

        db.Session.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<NotesSession>());
        (await db.Notes.CountAsync()).Should().Be(0);
    }

    public sealed class NotesSession;

    public sealed class SessionNotesContext(DbContextOptions<SessionNotesContext> options, NotesSession session) : DbContext(options)
    {
        public NotesSession Session { get; } = session;

        public DbSet<PooledNote> Notes => Set<PooledNote>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<PooledNote>().ToTable("Notes");
    }
}

/// <summary>
///     A context registered with <c>AddDbContextPerTenantDatabase</c> must only ever read and write the database of
///     the tenant that is current.
/// </summary>
public abstract class DatabasePerTenantTests(bool pooled) : IAsyncLifetime
{
    protected static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    protected static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

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

        instances.Should().HaveCount(pooled ? 1 : 3, pooled ? "the one pooled instance serves every lease" : "every scope has its own");
        seen.Should().Equal("acme:1", "globex:1", "acme:2");
        RowsIn("acme").Should().Equal("acme:acme note", "acme:acme note");
        RowsIn("globex").Should().Equal("globex:globex note");
    }

    [Fact]
    public async Task Factory_ConnectsEachContextToTheCurrentTenantsDatabase()
    {
        await using var services = Build();
        var factory = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>();
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();

        using (ambient.Use(Globex))
        {
            await using var db = factory.CreateDbContext();
            db.Notes.Add(new PooledNote { Text = "sync lease" });
            await db.SaveChangesAsync();
        }

        using (ambient.Use(Acme))
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
    public async Task CreatingAContext_WithoutACurrentTenant_Throws()
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
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();
        PooledNotesContext db;

        using (ambient.Use(Acme))
        {
            db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
        }

        await using (db)
        using (ambient.Use(Globex))
        {
            db.Notes.Add(new PooledNote { Text = "globex row in acme's database" });
            var act = () => db.SaveChangesAsync();

            var thrown = (await act.Should().ThrowAsync<TenantIsolationViolationException>())
                .WithMessage("*tenant 'acme'*current tenant is 'globex'*").Which;
            thrown.Kind.Should().Be(TenantIsolationViolationKind.TenantDatabaseMismatch);
            thrown.TypeName.Should().Be(nameof(PooledNotesContext));
            thrown.OffendingTenantId.Should().Be("acme", "the database belongs to acme");
            thrown.ExpectedTenantId.Should().Be("globex");
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
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();
        var factory = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>();
        PooledNotesContext db;

        using (ambient.Use(Acme))
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
        using (ambient.Use(Globex))
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
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();

        using (ambient.Use(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();

            if (openConnection)
            {
                await db.Database.OpenConnectionAsync();
            }

            var note = new PooledNote { Text = "added by acme" };
            db.Notes.Add(note);

            using (ambient.Use(Globex))
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
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();
        PooledNotesContext db;

        using (ambient.Use(Acme))
        {
            db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
        }

        await using (db)
        {
            var act = () => db.SaveChangesAsync();

            var thrown = (await act.Should().ThrowAsync<TenantIsolationViolationException>()).WithMessage("*current tenant is '(none)'*").Which;
            thrown.Kind.Should().Be(TenantIsolationViolationKind.TenantDatabaseMismatch);
            thrown.OffendingTenantId.Should().Be("acme");
            thrown.ExpectedTenantId.Should().BeNull("no tenant is current");
        }
    }

    [Theory]
    [InlineData("OpenConnection")]
    [InlineData("BeginTransaction")]
    public async Task ContextWithAnOpenConnection_UsedByItsOwnTenant_RunsCommands(string openedBy)
    {
        await using var services = Build();
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();

        using (ambient.Use(Acme))
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
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();

        using (ambient.Use(Globex))
        {
            await using var seed = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
            seed.Notes.Add(new PooledNote { Text = "globex's own note" });
            await seed.SaveChangesAsync();
        }

        using (ambient.Use(Acme))
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
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();

        using (ambient.Use(Globex))
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

        using (services.GetRequiredService<ITenantContextSetter<string>>().Use(Acme))
        {
            await using (var db = await factory.CreateDbContextAsync())
            {
                (await db.Notes.CountAsync()).Should().Be(0);
            }

            var sync = () => factory.CreateDbContext();
            sync.Should().Throw<InvalidOperationException>().WithMessage("*GetAsync*");
        }
    }

    [Fact]
    public void WithoutConnectionStrings_FailsAtRegistration_WithGuidance()
    {
        ServiceCollection services = new();

        services.Invoking(collection => collection.AddTenantry<string>(tenant => tenant
                .AddDbContextPerTenantDatabase<PooledNotesContext>((_, options) => options.UseSqlite(), pooled)))
            .Should().Throw<InvalidOperationException>().WithMessage("*PooledNotesContext*call UseConnectionStrings before it*");
    }

    [Fact]
    public void SameContextTwice_FailsAtRegistration()
    {
        ServiceCollection services = new();

        services.Invoking(collection => collection.AddTenantry<string>(tenant => tenant
                .UseConnectionStrings(options => options.GetConnectionString = t => t.TenantId)
                .AddDbContextPerTenantDatabase<PooledNotesContext>((_, options) => options.UseSqlite(), pooled)
                .AddDbContextPerTenantDatabase<PooledNotesContext>((_, options) => options.UseSqlite(), pooled)))
            .Should().Throw<InvalidOperationException>().WithMessage("*already called*");
    }

    [Fact]
    public async Task ContextWhoseOptionsAlsoUseTenantry_IsWiredOnce()
    {
        await using var services = Build(useTenantryToo: true);
        var ambient = services.GetRequiredService<ITenantContextSetter<string>>();

        using (ambient.Use(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
            db.Notes.Add(new PooledNote { Text = "once" });
            await db.SaveChangesAsync();

            db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!
                .Should().HaveCount(3, "the database guard, and Tenantry's save and query interceptors");
        }

        RowsIn("acme").Should().Equal("acme:once");
    }

    [Fact]
    public async Task PoolSize_ReachesThePool()
    {
        await using var services = Build(poolSize: 7);

        using (services.GetRequiredService<ITenantContextSetter<string>>().Use(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();

            db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.MaxPoolSize.Should().Be(pooled ? 7 : null);
        }
    }

    [Fact]
    public async Task ScopedContext_HasItsScopeAsItsApplicationServiceProvider_UnlessPooled()
    {
        await using var services = Build();
        await using var scope = services.GetRequiredService<ITenantScopeFactory<string>>().CreateScope(Acme);
        var db = scope.ServiceProvider.GetRequiredService<PooledNotesContext>();

        var applicationServices = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.ApplicationServiceProvider;

        // As with AddDbContext and AddDbContextPool: a pool's options, and so its contexts, are built once.
        (applicationServices == scope.ServiceProvider).Should().Be(!pooled);
    }

    [Fact]
    public async Task InterceptorsAddedInTheConfiguration_SeeNewEntitiesAlreadyStamped()
    {
        StampObserver observer = new();
        await using var services = Build(interceptor: observer);

        using (services.GetRequiredService<ITenantContextSetter<string>>().Use(Acme))
        {
            await using var db = services.GetRequiredService<IDbContextFactory<PooledNotesContext>>().CreateDbContext();
            db.Notes.Add(new PooledNote { Text = "observed" });
            await db.SaveChangesAsync();
        }

        observer.TenantIds.Should().Equal("acme");
    }

    private sealed class StampObserver : SaveChangesInterceptor
    {
        public List<string> TenantIds { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            TenantIds.AddRange(eventData.Context!.ChangeTracker.Entries<PooledNote>().Select(entry => entry.Entity.TenantId));
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    protected ServiceProvider Build(
        int poolSize = 1024,
        bool asyncOnly = false,
        bool useTenantryToo = false,
        Action<IServiceCollection>? configure = null,
        bool context = true,
        IInterceptor? interceptor = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddTenantry<string>(tenant =>
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

            if (context)
            {
                tenant.AddDbContextPerTenantDatabase<PooledNotesContext>(
                    (_, options) =>
                    {
                        options.UseSqlite();

                        if (interceptor is not null)
                        {
                            options.AddInterceptors(interceptor);
                        }

                        if (useTenantryToo)
                        {
                            options.UseTenantry();
                        }
                    },
                    pooled,
                    poolSize);
            }
            else
            {
                tenant.AddDbContextPerTenantDatabase<NonPooledDatabasePerTenantTests.SessionNotesContext>(
                    (_, options) => options.UseSqlite());
            }
        });

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
