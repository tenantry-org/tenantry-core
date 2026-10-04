using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// The join rows of a many-to-many relationship name the rows they join and carry no tenant of their own. A save that
/// adds, changes or deletes one confirms that each tenant-owned end it names is the current tenant's row as stored, as
/// an owned row is confirmed through its owner, so a stub with another tenant's key cannot add, change or delete that
/// tenant's join rows. A tenant-owned join entity is checked by its own TenantId instead.
/// </summary>
public sealed class ManyToManyTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();
    private readonly StatementCounter _statements = new();

    public void Dispose() => _connection.Dispose();

    // ── The attack: stubs that carry another tenant's keys with the current tenant's TenantId ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThroughStubsOfAnotherTenantsRows_ItsJoinRowsAreNotDeleted(bool sync)
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        Post stub = new() { Id = 1, TenantId = "globex", Tags = { new Tag { Id = 1, TenantId = "globex" } } };
        db.Attach(stub);
        stub.Tags.Clear();

        await Refused(db, sync);
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThroughStubsOfAnotherTenantsRows_NoJoinRowIsAddedBetweenThem(bool sync)
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        Post post = new() { Id = 1, TenantId = "globex" };
        Tag tag = new() { Id = 3, TenantId = "globex" };
        db.AttachRange(post, tag);
        post.Tags.Add(tag);

        await Refused(db, sync);
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThroughAStubOfAnotherTenantsRow_NoJoinRowLinksTheTenantsOwnRowToIt(bool sync)
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        db.Add(new Post { Id = 10 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var own = await db.Set<Post>().SingleAsync(TestContext.Current.CancellationToken);
        Tag stub = new() { Id = 1, TenantId = "globex" };
        db.Attach(stub);
        own.Tags.Add(stub);

        await Refused(db, sync);
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task AStubThatNamesAnotherTenant_IsRefusedBeforeAnythingIsSent()
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        Post stub = new() { Id = 1, TenantId = "acme", Tags = { new Tag { Id = 1, TenantId = "acme" } } };
        db.Attach(stub);
        stub.Tags.Clear();
        _statements.Reset();

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>()
            .Where(e => e.Kind == TenantIsolationViolationKind.EntityWrite && e.OffendingTenantId == "acme");
        _statements.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEndWhoseTenantIdWasChangedUnseen_IsRefused_AndIsNotMovedToTheOtherTenant(bool sync)
    {
        await SeedAsync();

        _tenant.As("acme");
        await using (var db = await CreateAsync<ImplicitContext>())
        {
            var post = await db.Set<Post>().SingleAsync(TestContext.Current.CancellationToken);
            await db.Set<Tag>().SingleAsync(t => t.Id == 3, TestContext.Current.CancellationToken);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            post.TenantId = "globex";
            db.Set<Dictionary<string, object>>("PostTag").Add(new Dictionary<string, object> { ["PostsId"] = 1, ["TagsId"] = 3 });

            Func<Task> save = sync ? () => Task.FromResult(db.SaveChanges()) : () => db.SaveChangesAsync();
            (await save.Should().ThrowAsync<TenantIsolationViolationException>()).Which.OffendingTenantId.Should().Be("globex");
        }

        await using var check = await CreateAsync<ImplicitContext>();
        (await check.Set<Post>().IgnoreQueryFilters().Select(p => p.TenantId).SingleAsync(TestContext.Current.CancellationToken))
            .Should().Be("acme");
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task AStubOfAnotherTenantsEnd_WhoseOriginalTenantIdIsForged_MatchesNoRow()
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        var post = db.Attach(new Post { Id = 1, TenantId = "acme" });
        post.Property(p => p.TenantId).OriginalValue = "globex";
        post.Property(p => p.TenantId).CurrentValue = "globex";
        post.Entity.Tags.Add(db.Attach(new Tag { Id = 3, TenantId = "globex" }).Entity);

        await Refused(db, sync: false);
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task AJoinRowWhoseEndsAreNotTracked_IsRefused()
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        db.Set<Dictionary<string, object>>("PostTag").Add(new Dictionary<string, object> { ["PostsId"] = 1, ["TagsId"] = 3 });

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("*PostTag*without*Post*");
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    // ── The tenant's own rows ──

    [Fact]
    public async Task TheTenantsOwnLinks_AreAddedAndRemoved_ConfirmingEachEndOnce()
    {
        await SeedAsync();

        _tenant.As("acme");
        await using var db = await CreateAsync<ImplicitContext>();
        var post = await db.Set<Post>().Include(p => p.Tags).SingleAsync(TestContext.Current.CancellationToken);
        var tag = await db.Set<Tag>().SingleAsync(t => t.Id == 3, TestContext.Current.CancellationToken);
        post.Tags.RemoveAll(t => t.Id == 1);
        post.Tags.Add(tag);
        _statements.Reset();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await LinksAsync()).Should().Equal("1-2", "1-3");

        // One write-back of TenantId for each end the save's join rows name: post 1, tag 1 and tag 3.
        _statements.Updates.Should().Be(3);
    }

    [Fact]
    public async Task ManyLinksToOneEnd_ConfirmItOnce()
    {
        await SeedAsync();

        _tenant.As("acme");
        await using var db = await CreateAsync<ImplicitContext>();
        var post = await db.Set<Post>().SingleAsync(TestContext.Current.CancellationToken);
        for (var id = 20; id < 25; id++)
        {
            post.Tags.Add(db.Add(new Tag { Id = id }).Entity);
        }

        _statements.Reset();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _statements.Updates.Should().Be(1, "only the post is confirmed; the new tags' INSERTs are their check");
        (await LinksAsync()).Should().HaveCount(7);
    }

    [Fact]
    public async Task NewEndsWithNewLinks_NeedNoConfirmation()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync<ImplicitContext>();
        db.Add(new Post { Id = 1, Tags = { new Tag { Id = 1 }, new Tag { Id = 2 } } });
        _statements.Reset();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _statements.Updates.Should().Be(0);
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task DeletingAnEnd_DeletesItsLinks_AndThroughAStub_NothingIsDeleted()
    {
        await SeedAsync();

        _tenant.As("globex");
        await using (var db = await CreateAsync<ImplicitContext>())
        {
            Post stub = new() { Id = 1, TenantId = "globex", Tags = { new Tag { Id = 1, TenantId = "globex" } } };
            db.Attach(stub);
            db.Remove(stub);

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await LinksAsync()).Should().Equal("1-1", "1-2");

        _tenant.As("acme");
        await using (var db = await CreateAsync<ImplicitContext>())
        {
            db.Remove(await db.Set<Post>().Include(p => p.Tags).SingleAsync(TestContext.Current.CancellationToken));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await LinksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeletingAnEndWhoseLinksAreNotLoaded_LeavesThemToTheDatabase()
    {
        await SeedAsync();

        _tenant.As("acme");
        await using var db = await CreateAsync<ImplicitContext>();
        db.Remove(await db.Set<Tag>().SingleAsync(t => t.Id == 1, TestContext.Current.CancellationToken));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await LinksAsync()).Should().Equal("1-2");
    }

    // ── No current tenant ──

    [Fact]
    public async Task WithoutATenant_AJoinRowWriteIsRejectedByDefault()
    {
        await SeedAsync();

        _tenant.As("acme");
        await using var db = await CreateAsync<ImplicitContext>();
        var post = await db.Set<Post>().Include(p => p.Tags).SingleAsync(TestContext.Current.CancellationToken);
        post.Tags.Clear();
        _tenant.AsNone();

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantNotResolvedException>().WithMessage("*PostTag*");
        (await LinksAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task WithoutATenant_UnderAllow_AJoinRowIsWrittenUnchecked()
    {
        await SeedAsync();

        _tenant.AsNone();
        await using var db = await CreateAsync<ImplicitContext>(new EfCoreIsolationOptions { OnMissingTenant = MissingTenantBehavior.Allow });
        Post post = new() { Id = 1, TenantId = "acme" };
        Tag tag = new() { Id = 3, TenantId = "acme" };
        db.AttachRange(post, tag);
        post.Tags.Add(tag);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await LinksAsync()).Should().Equal("1-1", "1-2", "1-3");
    }

    // ── Other shapes ──

    [Fact]
    public async Task AnExplicitJoinClassWithoutATenant_IsGuardedThroughItsEnds()
    {
        await SeedAsync<ExplicitContext>();

        _tenant.As("globex");
        await using var db = await CreateAsync<ExplicitContext>();
        Post stub = new() { Id = 1, TenantId = "globex", Tags = { new Tag { Id = 1, TenantId = "globex" } } };
        db.Attach(stub);
        stub.Tags.Clear();

        await Refused(db, sync: false);
        (await LinksAsync("PlainPostTag", "PostId", "TagId")).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task AJoinRowWithDataOfItsOwn_IsChangedOnlyThroughTheCurrentTenantsEnds()
    {
        await SeedAsync<PayloadContext>();

        _tenant.As("globex");
        await using (var db = await CreateAsync<PayloadContext>())
        {
            // A stub of acme's join row, with its ends as stubs too, all naming globex.
            db.AttachRange(new Post { Id = 1, TenantId = "globex" }, new Tag { Id = 1, TenantId = "globex" });
            var join = db.Attach(new NotedPostTag { PostId = 1, TagId = 1, Note = "seeded" });
            join.Entity.Note = "changed by globex";

            await Refused(db, sync: false);
        }

        _tenant.As("acme");
        await using (var db = await CreateAsync<PayloadContext>())
        {
            var join = await db.Set<NotedPostTag>().SingleAsync(j => j.PostId == 1 && j.TagId == 1, TestContext.Current.CancellationToken);
            join.Note.Should().Be("seeded");
            await db.Set<Post>().LoadAsync(TestContext.Current.CancellationToken);
            await db.Set<Tag>().LoadAsync(TestContext.Current.CancellationToken);
            join.Note = "changed by acme";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var check = await CreateAsync<PayloadContext>();
        (await check.Set<NotedPostTag>().SingleAsync(j => j.PostId == 1 && j.TagId == 1, TestContext.Current.CancellationToken))
            .Note.Should().Be("changed by acme");
    }

    [Fact]
    public async Task AJoinRowWithDataOfItsOwn_ChangedWithoutItsEnds_IsRefused()
    {
        await SeedAsync<PayloadContext>();

        _tenant.As("globex");
        await using var db = await CreateAsync<PayloadContext>();
        var join = db.Attach(new NotedPostTag { PostId = 1, TagId = 1, Note = "seeded" });
        join.Entity.Note = "changed by globex";

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("*NotedPostTag*without*");
    }

    [Fact]
    public async Task ATenantOwnedJoinClass_IsCheckedByItsOwnTenantId_AndItsEndsAreNotConfirmed()
    {
        await SeedAsync<TenantJoinContext>();

        _tenant.As("acme");
        await using (var db = await CreateAsync<TenantJoinContext>())
        {
            (await db.Set<PostTag>().IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken))
                .Should().HaveCount(2).And.OnlyContain(join => join.TenantId == "acme");
            var post = await db.Set<Post>().Include(p => p.Tags).SingleAsync(TestContext.Current.CancellationToken);
            post.Tags.RemoveAll(tag => tag.Id == 1);
            _statements.Reset();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            _statements.Updates.Should().Be(0, "the join row's DELETE carries its own TenantId");
        }

        _tenant.As("globex");
        await using (var db = await CreateAsync<TenantJoinContext>())
        {
            db.Remove(new PostTag { PostId = 1, TagId = 2, TenantId = "globex" });
            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
            db.ChangeTracker.Clear();
            (await db.Set<PostTag>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        }

        (await LinksAsync("PostTag", "PostId", "TagId")).Should().Equal("1-2");
    }

    [Fact]
    public async Task AnEndThatIsShared_IsNotConfirmed_AndTheOtherTenantsReadThroughItOnlyTheirOwnRows()
    {
        _tenant.As("acme");
        await using (var db = await CreateAsync<SharedEndContext>())
        {
            db.Add(new Label { Id = 1 });
            db.Add(new Article { Id = 1 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();
            var article = await db.Set<Article>().SingleAsync(TestContext.Current.CancellationToken);
            var label = await db.Set<Label>().SingleAsync(TestContext.Current.CancellationToken);
            article.Labels.Add(label);
            _statements.Reset();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            _statements.Updates.Should().Be(1, "only the article, the tenant-owned end, is confirmed");
        }

        _tenant.As("globex");
        await using (var db = await CreateAsync<SharedEndContext>())
        {
            (await db.Set<Label>().Include(l => l.Articles).SingleAsync(TestContext.Current.CancellationToken))
                .Articles.Should().BeEmpty("the articles' tenant filter applies through the shared label");

            Article stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            stub.Labels.Add(db.Set<Label>().Local.Single());
            await Refused(db, sync: false);
        }
    }

    [Fact]
    public async Task ASelfReferencingRelationship_ConfirmsBothEnds()
    {
        _tenant.As("acme");
        await using (var db = await CreateAsync<SelfContext>())
        {
            db.Add(new Person { Id = 1, Friends = { new Person { Id = 2 } } });
            db.Add(new Person { Id = 3 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _tenant.As("globex");
        await using (var db = await CreateAsync<SelfContext>())
        {
            Person one = new() { Id = 1, TenantId = "globex", Friends = { new Person { Id = 2, TenantId = "globex" } } };
            db.Attach(one);
            one.Friends.Clear();
            one.Friends.Add(db.Attach(new Person { Id = 3, TenantId = "globex" }).Entity);
            await Refused(db, sync: false);
        }

        (await LinksAsync("PersonPerson", "FriendOfId", "FriendsId")).Should().Equal("1-2");

        _tenant.As("acme");
        await using (var db = await CreateAsync<SelfContext>())
        {
            var one = await db.Set<Person>().Include(p => p.Friends).SingleAsync(p => p.Id == 1, TestContext.Current.CancellationToken);
            one.Friends.Add(await db.Set<Person>().SingleAsync(p => p.Id == 3, TestContext.Current.CancellationToken));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await LinksAsync("PersonPerson", "FriendOfId", "FriendsId")).Should().Equal("1-2", "1-3");
    }

    [Fact]
    public async Task EndsWithCompositeKeys_AreConfirmed()
    {
        _tenant.As("acme");
        await using (var db = await CreateAsync<CompositeContext>())
        {
            db.Add(new Document { Code = "a", Version = 1, Topics = { new Topic { Id = 1 } } });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _tenant.As("globex");
        await using (var db = await CreateAsync<CompositeContext>())
        {
            Document stub = new() { Code = "a", Version = 1, TenantId = "globex", Topics = { new Topic { Id = 1, TenantId = "globex" } } };
            db.Attach(stub);
            stub.Topics.Clear();
            await Refused(db, sync: false);
        }

        _tenant.As("acme");
        await using var check = await CreateAsync<CompositeContext>();
        (await check.Set<Document>().Include(d => d.Topics).SingleAsync(TestContext.Current.CancellationToken)).Topics.Should().ContainSingle();
    }

    [Fact]
    public async Task AnEndOfADerivedType_IsConfirmed()
    {
        _tenant.As("acme");
        await using (var db = await CreateAsync<DerivedEndContext>())
        {
            db.Add(new Video { Id = 1, Keywords = { new Keyword { Id = 1 } } });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _tenant.As("globex");
        await using (var db = await CreateAsync<DerivedEndContext>())
        {
            Video stub = new() { Id = 1, TenantId = "globex", Keywords = { new Keyword { Id = 1, TenantId = "globex" } } };
            db.Attach(stub);
            stub.Keywords.Clear();
            await Refused(db, sync: false);
        }

        _tenant.As("acme");
        await using var check = await CreateAsync<DerivedEndContext>();
        (await check.Set<Video>().Include(v => v.Keywords).SingleAsync(TestContext.Current.CancellationToken)).Keywords.Should().ContainSingle();
    }

    [Fact]
    public async Task AJoinThroughAnAlternateKeyWithTheTenantId_NeedsNoConfirmationOfThatEnd()
    {
        _tenant.As("acme");
        await using (var db = await CreateAsync<TenantKeyContext>())
        {
            db.Add(new Shelf { Id = 1, Code = "s1", Books = { new Book { Id = 1 } } });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();
            var shelf = await db.Set<Shelf>().Include(s => s.Books).SingleAsync(TestContext.Current.CancellationToken);
            shelf.Books.Clear();
            _statements.Reset();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            _statements.Updates.Should().Be(1, "the book is confirmed; the shelf's key in the join row names its tenant");
        }
    }

    [Fact]
    public void AJoinThroughAnAlternateKeyWithoutTheTenantId_IsRefused()
    {
        using AlternateKeyContext db = new(Options<AlternateKeyContext>());

        db.Invoking(d => d.Set<Shelf>().ToList()).Should().Throw<TenantIsolationViolationException>()
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration)
            .WithMessage("*names 'Shelf' through a key*");
    }

    [Fact]
    public void AJoinClassKeyedByAKeyOfItsOwn_IsRefused()
    {
        using KeyedJoinContext db = new(Options<KeyedJoinContext>());

        db.Invoking(d => d.Set<Post>().ToList()).Should().Throw<TenantIsolationViolationException>()
            .Where(e => e.Kind == TenantIsolationViolationKind.ModelConfiguration)
            .WithMessage("*'KeyedPostTag'*key that does not include its foreign key*");
    }

    [Fact]
    public async Task AUnidirectionalRelationship_IsGuardedThroughBothEnds()
    {
        _tenant.As("acme");
        await using (var db = await CreateAsync<UnidirectionalContext>())
        {
            db.Add(new Board { Id = 1, Badges = { new Badge { Id = 1 } } });
            db.Add(new Badge { Id = 2 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _tenant.As("globex");
        await using (var db = await CreateAsync<UnidirectionalContext>())
        {
            // A stub of acme's badge on globex's own board: the badge, the end without a navigation, is confirmed.
            db.Add(new Board { Id = 5 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.Set<Board>().Local.Single().Badges.Add(db.Attach(new Badge { Id = 2, TenantId = "globex" }).Entity);
            await Refused(db, sync: false);
        }

        _tenant.As("acme");
        await using var check = await CreateAsync<UnidirectionalContext>();
        (await check.Set<Board>().Include(b => b.Badges).SingleAsync(TestContext.Current.CancellationToken)).Badges.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteDeleteOnTheJoinTable_IsNotGuarded()
    {
        await SeedAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<ImplicitContext>();
        var deleted = await db.Set<Dictionary<string, object>>("PostTag").ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        deleted.Should().Be(2, "the join entity has no tenant filter: bulk writes of its rows are outside the guard");
    }

    // ── Helpers ──

    private static async Task Refused(DbContext db, bool sync)
    {
        Func<Task> save = sync ? () => Task.FromResult(db.SaveChanges()) : () => db.SaveChangesAsync();

        // A stub naming the current tenant passes the in-memory check; the write-back of its TenantId matches no row.
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private async Task SeedAsync() => await SeedAsync<ImplicitContext>();

    // Acme: post 1 linked to tags 1 and 2, and tag 3 linked to nothing.
    private async Task SeedAsync<TContext>()
        where TContext : DbContext
    {
        _tenant.As("acme");
        await using var db = await CreateAsync<TContext>();
        db.Add(new Post { Id = 1, Tags = { new Tag { Id = 1 }, new Tag { Id = 2 } } });
        db.Add(new Tag { Id = 3 });
        await db.SaveChangesAsync();

        if (db is PayloadContext)
        {
            await db.Set<NotedPostTag>().ExecuteUpdateAsync(s => s.SetProperty(j => j.Note, "seeded"));
        }
    }

    // The join rows as stored, as "left-right".
    private async Task<List<string>> LinksAsync(string table = "PostTag", string left = "PostsId", string right = "TagsId")
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT \"{left}\", \"{right}\" FROM \"{table}\" ORDER BY 1, 2";
        await using var reader = await command.ExecuteReaderAsync();
        List<string> rows = [];

        while (await reader.ReadAsync())
        {
            rows.Add($"{reader.GetInt32(0)}-{reader.GetInt32(1)}");
        }

        return rows;
    }

    private async Task<TContext> CreateAsync<TContext>(EfCoreIsolationOptions? isolation = null)
        where TContext : DbContext
    {
        var db = (TContext)Activator.CreateInstance(typeof(TContext), Options<TContext>(isolation))!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private DbContextOptions<TContext> Options<TContext>(EfCoreIsolationOptions? isolation = null)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>(DbContextFactory.Options<TContext>(_tenant, _connection, isolation))
            .AddInterceptors(_statements)
            .Options;

    // Counts the statements sent, and the UPDATEs among them.
    private sealed class StatementCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public int Updates { get; private set; }

        public void Reset() => Count = Updates = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Note(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Note(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Note(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Note(command);
            return ValueTask.FromResult(result);
        }

        private void Note(DbCommand command)
        {
            if (command.CommandText.StartsWith("PRAGMA", StringComparison.Ordinal) ||
                command.CommandText.Contains("sqlite_master", StringComparison.Ordinal))
            {
                return;
            }

            Count++;

            // EF Core batches a save's statements into one command: count each UPDATE in it.
            Updates += command.CommandText.Split("UPDATE ").Length - 1;
        }
    }

    // ── Model ──

    public class Post : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Tag> Tags { get; } = [];
    }

    public class Tag : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Post> Posts { get; } = [];
    }

    public sealed class Board : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Badge> Badges { get; } = [];
    }

    public sealed class Badge : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Keyword : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Video> Videos { get; } = [];
    }

    public sealed class PostTag : ITenantEntity<string>
    {
        public int PostId { get; set; }

        public int TagId { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class PlainPostTag
    {
        public int PostId { get; set; }

        public int TagId { get; set; }
    }

    public sealed class NotedPostTag
    {
        public int PostId { get; set; }

        public int TagId { get; set; }

        [MaxLength(64)]
        public string Note { get; set; } = string.Empty;
    }

    public sealed class KeyedPostTag
    {
        public int Id { get; set; }

        public int PostId { get; set; }

        public int TagId { get; set; }
    }

    public sealed class Article : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Label> Labels { get; } = [];
    }

    public sealed class Label
    {
        public int Id { get; set; }

        public List<Article> Articles { get; } = [];
    }

    public sealed class Person : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Person> Friends { get; } = [];

        public List<Person> FriendOf { get; } = [];
    }

    public sealed class Document : ITenantEntity<string>
    {
        [MaxLength(16)]
        public string Code { get; set; } = string.Empty;

        public int Version { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Topic> Topics { get; } = [];
    }

    public sealed class Topic : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Document> Documents { get; } = [];
    }

    public abstract class Content : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Video : Content
    {
        public List<Keyword> Keywords { get; } = [];
    }

    public sealed class Shelf : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        [MaxLength(16)]
        public string Code { get; set; } = string.Empty;

        public List<Book> Books { get; } = [];
    }

    public sealed class Book : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Shelf> Shelves { get; } = [];
    }

    // ── Contexts (one type per model, because EF Core caches the model per context type) ──

    public sealed class ImplicitContext(DbContextOptions<ImplicitContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts);
        }
    }

    public sealed class UnidirectionalContext(DbContextOptions<UnidirectionalContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Board>().HasMany(b => b.Badges).WithMany();
        }
    }

    public sealed class ExplicitContext(DbContextOptions<ExplicitContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<PlainPostTag>();
        }
    }

    public sealed class PayloadContext(DbContextOptions<PayloadContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<NotedPostTag>(
                right => right.HasOne<Tag>().WithMany().HasForeignKey(j => j.TagId),
                left => left.HasOne<Post>().WithMany().HasForeignKey(j => j.PostId),
                join => join.HasKey(j => new { j.PostId, j.TagId }));
        }
    }

    public sealed class KeyedJoinContext(DbContextOptions<KeyedJoinContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<KeyedPostTag>(
                right => right.HasOne<Tag>().WithMany().HasForeignKey(j => j.TagId),
                left => left.HasOne<Post>().WithMany().HasForeignKey(j => j.PostId),
                join => join.HasKey(j => j.Id));
        }
    }

    public sealed class TenantJoinContext(DbContextOptions<TenantJoinContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<PostTag>();
        }
    }

    public sealed class SharedEndContext(DbContextOptions<SharedEndContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Article>().HasMany(a => a.Labels).WithMany(l => l.Articles);
            modelBuilder.Entity<Label>().Property(l => l.Id).ValueGeneratedNever();
        }
    }

    public sealed class SelfContext(DbContextOptions<SelfContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Person>().HasMany(p => p.Friends).WithMany(p => p.FriendOf);
    }

    public sealed class CompositeContext(DbContextOptions<CompositeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Document>().HasKey(d => new { d.Code, d.Version });
            modelBuilder.Entity<Document>().HasMany(d => d.Topics).WithMany(t => t.Documents);
        }
    }

    public sealed class DerivedEndContext(DbContextOptions<DerivedEndContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Content>();
            modelBuilder.Entity<Video>().HasMany(v => v.Keywords).WithMany(k => k.Videos);
        }
    }

    public sealed class TenantKeyContext(DbContextOptions<TenantKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Shelf>().HasAlternateKey(s => new { s.TenantId, s.Code });
            modelBuilder.Entity<Shelf>().HasMany(s => s.Books).WithMany(b => b.Shelves).UsingEntity(
                "ShelfBook",
                right => right.HasOne(typeof(Book)).WithMany().HasForeignKey("BookId"),
                left => left.HasOne(typeof(Shelf)).WithMany().HasForeignKey("ShelfTenantId", "ShelfCode")
                    .HasPrincipalKey(nameof(Shelf.TenantId), nameof(Shelf.Code)));
        }
    }

    public sealed class AlternateKeyContext(DbContextOptions<AlternateKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Shelf>().HasAlternateKey(s => s.Code);
            modelBuilder.Entity<Shelf>().HasMany(s => s.Books).WithMany(b => b.Shelves).UsingEntity(
                "ShelfBook",
                right => right.HasOne(typeof(Book)).WithMany().HasForeignKey("BookId"),
                left => left.HasOne(typeof(Shelf)).WithMany().HasForeignKey("ShelfCode").HasPrincipalKey(nameof(Shelf.Code)));
        }
    }
}
