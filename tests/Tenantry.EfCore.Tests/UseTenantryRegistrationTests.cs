using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tenantry.EfCore.Internal;

namespace Tenantry.EfCore.Tests;

/// <summary>
/// What <c>UseTenantry()</c> adds to a context's options, and the optional isolation options.
/// </summary>
public sealed class UseTenantryRegistrationTests : IDisposable
{
    private readonly SqliteConnection _connection = DbContextFactory.CreateSharedConnection();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task WithoutConfigureEfCoreIsolation_SavesUseTheRejectPolicy()
    {
        await using var db = await DbContextFactory.CreateContextAsync(TestTenantContext.Empty().AsNone(), _connection);
        db.Orders.Add(new Order { Description = "no tenant" });

        await db.Awaiting(context => context.SaveChangesAsync()).Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public async Task UseTenantryWithOptions_RelaxesOnlyThatContext()
    {
        var services = DbContextFactory.Services(TestTenantContext.Empty().AsNone());

        DbContextOptions<TestDbContext> Options(bool maintenance)
        {
            var builder = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).UseApplicationServiceProvider(services);
            return (maintenance ? builder.UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Allow) : builder.UseTenantry()).Options;
        }

        await using (TestDbContext setup = new(Options(maintenance: false)))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await using TestDbContext maintenance = new(Options(maintenance: true));
        maintenance.Orders.Add(new Order { Description = "for acme", TenantId = "acme" });
        await maintenance.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using TestDbContext ordinary = new(Options(maintenance: false));
        ordinary.Orders.Add(new Order { Description = "for acme", TenantId = "acme" });
        await ordinary.Awaiting(context => context.SaveChangesAsync()).Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public void UseTenantryWithOptions_StartsFromTheApplicationsOptions()
    {
        var services = DbContextFactory.Services(
            TestTenantContext.Empty().AsNone(),
            new EfCoreIsolationOptions { OnSaveWithoutTransaction = SaveWithoutTransactionBehavior.Reject });

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Warn)
            .Options;

        var isolation = options.FindExtension<TenantryOptionsExtension>()!.Isolation!;
        isolation.OnMissingTenant.Should().Be(MissingTenantBehavior.Warn);
        isolation.OnSaveWithoutTransaction.Should().Be(SaveWithoutTransactionBehavior.Reject);
    }

    [Fact]
    public void ConfigureEfCoreIsolation_CapturesTheConfiguredPolicy()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant.ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<EfCoreIsolationOptions>>().Value.OnMissingTenant.Should().Be(MissingTenantBehavior.Warn);
    }

    [Fact]
    public void ConfigureEfCoreIsolation_CalledTwice_ConfiguresTheSameOptions()
    {
        ServiceCollection services = new();

        services.AddTenantry<string>(tenant => tenant
            .ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Allow)
            .ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<EfCoreIsolationOptions>>().Value.OnMissingTenant.Should().Be(MissingTenantBehavior.Warn);
    }

    [Fact]
    public void UseTenantry_AddsTheInterceptorsAndTheExtension()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).UseTenantry().Options;

        options.FindExtension<TenantryOptionsExtension>().Should().NotBeNull();
        options.FindExtension<CoreOptionsExtension>()!.Interceptors
            .Should().BeEquivalentTo(new IInterceptor[] { TenantSaveChangesInterceptor.Instance, TenantQueryInterceptor.Instance, TenantTransactionInterceptor.Instance });
    }

    [Fact]
    public void UseTenantry_CalledTwice_ChangesNothingTheSecondTime()
    {
        ContributorCount contributor = new();
        var services = DbContextFactory.Services<string>(
            new TestTenantContext(),
            configure: collection => collection.AddSingleton<ITenantDbContextOptionsContributor>(contributor));

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(services)
            .UseTenantry()
            .UseTenantry()
            .Options;

        options.FindExtension<CoreOptionsExtension>()!.Interceptors.Should().HaveCount(3);
        contributor.Calls.Should().Be(1);
    }

    [Fact]
    public void OptionsContributors_ConfigureEveryContextThatUsesTenantry()
    {
        ContributorCount contributor = new();
        ServiceCollection collection = new();
        collection.AddTenantry<string>();
        collection.AddSingleton<ITenantDbContextOptionsContributor>(contributor);
        collection.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection).UseTenantry());
        collection.AddDbContext<OtherContext>(options => options.UseSqlite(_connection));
        using var services = collection.BuildServiceProvider();
        using var scope = services.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<DbContextOptions<TestDbContext>>();
        _ = scope.ServiceProvider.GetRequiredService<DbContextOptions<OtherContext>>();

        contributor.ContextTypes.Should().Equal(typeof(TestDbContext));
    }

    [Fact]
    public void ContextsFromDifferentApplications_ShareOneEfCoreServiceProvider()
    {
        IModelCustomizer Customizer()
        {
            using TestDbContext db = new(DbContextFactory.Options<TestDbContext>(new TestTenantContext(), _connection));
            return db.GetService<IModelCustomizer>();
        }

        var first = Customizer();

        first.Should().BeOfType<TenantModelCustomizer>();
        Enumerable.Range(0, 25).Select(_ => Customizer()).Should().AllSatisfy(customizer => customizer.Should().BeSameAs(first));
    }

    private sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options);

    private sealed class ContributorCount : ITenantDbContextOptionsContributor
    {
        public int Calls { get; private set; }

        public List<Type> ContextTypes { get; } = [];

        public void Configure(DbContextOptionsBuilder optionsBuilder)
        {
            Calls++;
            ContextTypes.Add(optionsBuilder.Options.ContextType);
        }
    }
}
