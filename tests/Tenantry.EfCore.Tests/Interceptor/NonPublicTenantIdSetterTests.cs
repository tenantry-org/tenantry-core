using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;

namespace Tenantry.EfCore.Tests.Interceptor;

/// <summary>
/// <see cref="ITenantEntity{TKey}"/> needs only a getter: the interceptor sets <c>TenantId</c> through EF Core, so an
/// entity can keep the setter to itself.
/// </summary>
public sealed class NonPublicTenantIdSetterTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();
    private readonly TestTenantContext _tenant = TestTenantContext.Empty();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task NewEntities_WithAPrivateOrInitOnlySetter_AreStampedAndReadBackForTheirTenantOnly()
    {
        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            db.Private.Add(new PrivateSetterNote("acme note"));
            db.InitOnly.Add(new InitOnlyNote { Text = "acme note" });
            await db.SaveChangesAsync();
        }

        await using (var db = await CreateAsync(_tenant.As("globex")))
        {
            db.Private.Add(new PrivateSetterNote("globex note"));
            await db.SaveChangesAsync();

            (await db.Private.SingleAsync()).Should().Match<PrivateSetterNote>(n => n.TenantId == "globex" && n.Text == "globex note");
            (await db.InitOnly.CountAsync()).Should().Be(0);
        }

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            (await db.Private.SingleAsync()).TenantId.Should().Be("acme");
            (await db.InitOnly.SingleAsync()).TenantId.Should().Be("acme");
        }
    }

    [Fact]
    public async Task ChangingAnotherTenantsEntity_WithAPrivateSetter_IsStillRejected()
    {
        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            db.Private.Add(new PrivateSetterNote("acme note"));
            await db.SaveChangesAsync();
        }

        await using (var db = await CreateAsync(_tenant.As("acme")))
        {
            var note = await db.Private.SingleAsync();
            note.Text = "changed as globex";
            _tenant.As("globex");

            await db.Awaiting(d => d.SaveChangesAsync()).Should().ThrowAsync<TenantIsolationViolationException>();
        }
    }

    private async Task<SetterContext> CreateAsync(TestTenantContext tenant)
    {
        SetterContext db = new(DbContextFactory.Options<SetterContext>(tenant, _connection));
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    public sealed class PrivateSetterNote(string text) : ITenantEntity<string>
    {
        public int Id { get; private set; }

        [MaxLength(64)]
        public string TenantId { get; private set; } = string.Empty;

        [MaxLength(64)]
        public string Text { get; set; } = text;
    }

    public sealed class InitOnlyNote : ITenantEntity<string>
    {
        public int Id { get; init; }

        [MaxLength(64)]
        public string TenantId { get; init; } = string.Empty;

        [MaxLength(64)]
        public string Text { get; init; } = string.Empty;
    }

    private sealed class SetterContext(DbContextOptions<SetterContext> options)
        : DbContext(options)
    {
        public DbSet<PrivateSetterNote> Private => Set<PrivateSetterNote>();

        public DbSet<InitOnlyNote> InitOnly => Set<InitOnlyNote>();
    }
}
