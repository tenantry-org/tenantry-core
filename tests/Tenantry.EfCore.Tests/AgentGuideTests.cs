using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.EfCore.Tests;

/// <summary>
/// The isolation test docs/ai-agents.md gives an application ("Verify isolation"), run against the application's
/// registration with and without <c>UseTenantry()</c>: it passes with it, and fails without it, which is what makes it
/// worth writing. Keep the two in step.
/// </summary>
public sealed class AgentGuideTests
{
    private static readonly TenantDescriptor<Guid> Acme = new() { TenantId = Guid.NewGuid(), Name = "Acme" };
    private static readonly TenantDescriptor<Guid> Globex = new() { TenantId = Guid.NewGuid(), Name = "Globex" };

    [Fact]
    public async Task TheGuidesTest_PassesWithTheApplicationsRegistration() =>
        await GuideTestAsync(isolated: true);

    [Fact]
    public async Task TheGuidesTest_FailsWhenTheApplicationsRegistrationLosesUseTenantry() =>
        await FluentActions.Awaiting(() => GuideTestAsync(isolated: false))
            .Should().ThrowAsync<Exception>("an application that forgot UseTenantry() must fail the test");

    // The guide's EachTenantSeesOnlyItsOwnRows_AndAnotherTenantsRowIsRefused, with xUnit's asserts.
    private static async Task GuideTestAsync(bool isolated)
    {
        var ct = TestContext.Current.CancellationToken;
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var services = new ServiceCollection()
            .AddTenantry<Guid>(tenant => tenant.UseInMemoryStore([Acme, Globex]))
            .AddBillingDbContext(options => options.UseSqlite(connection), isolated)
            .BuildServiceProvider(validateScopes: true);

        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.EnsureCreatedAsync(ct);

        var scopes = services.GetRequiredService<ITenantScopeFactory<Guid>>();

        await scopes.RunInScopeAsync(Acme.TenantId, async (scope, token) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.Invoices.Add(new Invoice { Amount = 10 });
            await db.SaveChangesAsync(token);
        }, ct);

        await scopes.RunInScopeAsync(Globex.TenantId, async (scope, token) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            Assert.Empty(await db.Invoices.ToListAsync(token));

            db.Invoices.Add(new Invoice { TenantId = Acme.TenantId, Amount = 20 });
            await Assert.ThrowsAsync<TenantIsolationViolationException>(() => db.SaveChangesAsync(token));
        }, ct);
    }

    public sealed class Invoice : TenantEntity<Guid>
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }

    public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
    }
}

internal static class AgentGuideRegistration
{
    // The guide's AddBillingDbContext, which an application's Program.cs calls, here with UseTenantry() or without.
    public static IServiceCollection AddBillingDbContext(
        this IServiceCollection services, Action<DbContextOptionsBuilder> database, bool isolated) =>
        services.AddDbContext<AgentGuideTests.BillingDbContext>(options =>
        {
            database(options);

            if (isolated)
                options.UseTenantry();
        });
}
