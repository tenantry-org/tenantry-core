using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// The join rows of a many-to-many relationship name the two rows they join and nothing else, unless the join entity is
/// tenant-owned. A model whose join entity is not is refused when either end is tenant-owned. With a tenant-owned join
/// entity the rows are stamped, filtered and checked on write as any other tenant-owned row.
/// </summary>
public sealed class ManyToManyTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void AJoinEntityEFCoreCreates_BetweenTenantOwnedTypes_IsRefused()
    {
        using ImplicitJoinContext db = new(Options<ImplicitJoinContext>());

        var refused = db.Invoking(d => d.Model).Should().Throw<TenantIsolationViolationException>().Which;

        refused.Kind.Should().Be(TenantIsolationViolationKind.ModelConfiguration);
        refused.Message.Should().Contain("'Post'").And.Contain("'Tag'").And.Contain("UsingEntity<TJoin>()").And.Contain("ITenantEntity<String>");
    }

    [Fact]
    public void AJoinEntityThatIsNotTenantOwned_BetweenTenantOwnedTypes_IsRefused()
    {
        using PlainJoinContext db = new(Options<PlainJoinContext>());

        db.Invoking(d => d.Model).Should().Throw<TenantIsolationViolationException>()
            .Which.Message.Should().Contain("'PlainPostTag'");
    }

    [Fact]
    public void AJoinEntityThatIsNotTenantOwned_BetweenATenantOwnedTypeAndASharedOne_IsRefused()
    {
        using SharedEndContext db = new(Options<SharedEndContext>());

        var refused = db.Invoking(d => d.Model).Should().Throw<TenantIsolationViolationException>().Which;

        refused.Kind.Should().Be(TenantIsolationViolationKind.ModelConfiguration);
        refused.Message.Should().Contain("'Article'").And.Contain("'Label'");
    }

    [Fact]
    public async Task AJoinEntityEFCoreCreates_BetweenTypesThatAreNotTenantOwned_IsLeftAlone()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync<SharedEndsContext>();
        db.Add(new Country { Id = 1, Languages = { new Language { Id = 1 } } });
        db.Add(new Note());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _tenant.As("globex");
        db.ChangeTracker.Clear();
        (await db.Set<Country>().Include(c => c.Languages).SingleAsync(TestContext.Current.CancellationToken)).Languages.Should().ContainSingle();
    }

    [Fact]
    public async Task ATenantOwnedJoinEntity_IsStampedAndFiltered()
    {
        await SeedAcmeAsync();

        _tenant.As("acme");
        await using var db = await CreateAsync<TenantJoinContext>();
        var joins = await db.Set<PostTag>().IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
        joins.Should().HaveCount(2).And.OnlyContain(join => join.TenantId == "acme");
        (await db.Set<Post>().Include(p => p.Tags).SingleAsync(TestContext.Current.CancellationToken)).Tags.Should().HaveCount(2);

        _tenant.As("globex");
        db.ChangeTracker.Clear();
        (await db.Set<PostTag>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task ATenantOwnedJoinEntity_OfTheTenantsOwn_IsAddedAndRemovedThroughTheNavigation()
    {
        await SeedAcmeAsync();

        _tenant.As("acme");
        await using var db = await CreateAsync<TenantJoinContext>();
        var post = await db.Set<Post>().Include(p => p.Tags).SingleAsync(TestContext.Current.CancellationToken);
        post.Tags.RemoveAll(tag => tag.Id == 1);
        post.Tags.Add(new Tag { Id = 3 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await AcmeJoinsAsync()).Should().Equal("1-2", "1-3");
    }

    [Fact]
    public async Task ThroughStubsOfAnotherTenantsRows_ItsJoinRowsAreNotDeleted()
    {
        // Both stubs stay Unchanged, so neither is written and neither TenantId is checked. The join row is.
        await SeedAcmeAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<TenantJoinContext>();
        Post stub = new() { Id = 1, TenantId = "globex", Tags = { new Tag { Id = 1, TenantId = "globex" } } };
        db.Attach(stub);
        stub.Tags.Clear();

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<Exception>()
            .Where(e => e is TenantIsolationViolationException || e is DbUpdateConcurrencyException);
        (await AcmeJoinsAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task ThroughAStubOfAnotherTenantsJoinRow_ItIsNotDeleted()
    {
        await SeedAcmeAsync();

        _tenant.As("globex");
        await using var db = await CreateAsync<TenantJoinContext>();
        db.Remove(new PostTag { PostId = 1, TagId = 1, TenantId = "globex" });

        await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await AcmeJoinsAsync()).Should().Equal("1-1", "1-2");
    }

    [Fact]
    public async Task AJoinRowToAStubOfAnotherTenantsRow_IsTheCurrentTenants_AndTheOtherTenantDoesNotSeeIt()
    {
        await SeedAcmeAsync();

        _tenant.As("globex");

        await using (var db = await CreateAsync<TenantJoinContext>())
        {
            Tag stub = new() { Id = 1, TenantId = "globex" };
            db.Attach(stub);
            db.Add(new Post { Id = 2, Tags = { stub } });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _tenant.As("acme");
        await using var acme = await CreateAsync<TenantJoinContext>();
        (await acme.Set<Tag>().Include(t => t.Posts).SingleAsync(t => t.Id == 1, TestContext.Current.CancellationToken))
            .Posts.Select(p => p.Id).Should().Equal(1);
        (await AcmeJoinsAsync()).Should().Equal("1-1", "1-2");
    }

    private async Task SeedAcmeAsync()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync<TenantJoinContext>();
        db.Add(new Post { Id = 1, Tags = { new Tag { Id = 1 }, new Tag { Id = 2 } } });
        await db.SaveChangesAsync();
    }

    // Acme's join rows as stored, as "post-tag".
    private async Task<List<string>> AcmeJoinsAsync()
    {
        _tenant.As("acme");
        await using var db = await CreateAsync<TenantJoinContext>();
        var joins = await db.Set<PostTag>().AsNoTracking().OrderBy(j => j.PostId).ThenBy(j => j.TagId).ToListAsync();
        return [.. joins.Select(j => $"{j.PostId}-{j.TagId}")];
    }

    private async Task<TContext> CreateAsync<TContext>()
        where TContext : DbContext
    {
        var db = (TContext)Activator.CreateInstance(typeof(TContext), Options<TContext>())!;
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        DbContextFactory.Options<TContext>(_tenant, _connection);

    public sealed class Post : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Tag> Tags { get; } = [];
    }

    public sealed class Tag : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;

        public List<Post> Posts { get; } = [];
    }

    public sealed class PostTag : ITenantEntity<string>
    {
        public int PostId { get; set; }

        public int TagId { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class Note : ITenantEntity<string>
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class PlainPostTag
    {
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

    [SharedAcrossTenants]
    public sealed class Label
    {
        public int Id { get; set; }

        public List<Article> Articles { get; } = [];
    }

    [SharedAcrossTenants]
    public sealed class Country
    {
        public int Id { get; set; }

        public List<Language> Languages { get; } = [];
    }

    [SharedAcrossTenants]
    public sealed class Language
    {
        public int Id { get; set; }

        public List<Country> Countries { get; } = [];
    }

    public sealed class ImplicitJoinContext(DbContextOptions<ImplicitJoinContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts);
    }

    public sealed class PlainJoinContext(DbContextOptions<PlainJoinContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Post>().HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<PlainPostTag>();
    }

    public sealed class SharedEndContext(DbContextOptions<SharedEndContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Article>().HasMany(a => a.Labels).WithMany(l => l.Articles);
    }

    public sealed class SharedEndsContext(DbContextOptions<SharedEndsContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Country>(country =>
            {
                country.Property(c => c.Id).ValueGeneratedNever();
                country.HasMany(c => c.Languages).WithMany(l => l.Countries);
            });
            modelBuilder.Entity<Language>().Property(l => l.Id).ValueGeneratedNever();
            modelBuilder.Entity<Note>();
        }
    }

    public sealed class TenantJoinContext(DbContextOptions<TenantJoinContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(post =>
            {
                post.Property(p => p.Id).ValueGeneratedNever();
                post.HasMany(p => p.Tags).WithMany(t => t.Posts).UsingEntity<PostTag>();
            });
            modelBuilder.Entity<Tag>().Property(t => t.Id).ValueGeneratedNever();
        }
    }
}
