using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

public sealed class TenantConnectionStringProviderTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    [Fact]
    public void Get_UsesTheGivenTenant_AndNeedsNoCurrentTenant()
    {
        using var services = Build(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");

        Provider(services).Get(Acme).Should().Be("Database=app_acme");
    }

    [Fact]
    public async Task GetAsync_PrefersTheAsyncDelegate()
    {
        using var services = Build(options =>
        {
            options.GetConnectionString = _ => "sync";
            options.GetConnectionStringAsync = async (t, _) =>
            {
                await Task.Yield();
                return $"async {t.TenantId}";
            };
        });

        (await Provider(services).GetAsync(Acme, TestContext.Current.CancellationToken)).Should().Be("async acme");
        Provider(services).Get(Acme).Should().Be("sync", "the synchronous method uses the synchronous delegate");
    }

    [Fact]
    public async Task GetAsync_FallsBackToTheSyncDelegate()
    {
        using var services = Build(options => options.GetConnectionString = t => $"sync {t.TenantId}");

        (await Provider(services).GetAsync(Acme, TestContext.Current.CancellationToken)).Should().Be("sync acme");
    }

    [Fact]
    public async Task GetAsync_PassesTheCancellationToken()
    {
        CancellationToken received = default;
        using CancellationTokenSource cts = new();
        using var services = Build(options => options.GetConnectionStringAsync = (_, ct) =>
        {
            received = ct;
            return ValueTask.FromResult("x");
        });

        await Provider(services).GetAsync(Acme, cts.Token);

        received.Should().Be(cts.Token);
    }

    [Fact]
    public async Task CurrentTenantConnectionString_UsesTheCurrentTenant()
    {
        using var services = Build(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");
        var current = services.GetRequiredService<CurrentTenantConnectionString<string>>();

        using (services.GetRequiredService<ITenantContextSetter<string>>().Use(Acme))
        {
            current.Get().Should().Be("Database=app_acme");
            (await current.GetAsync(TestContext.Current.CancellationToken)).Should().Be("Database=app_acme");
        }
    }

    [Fact]
    public async Task CurrentTenantConnectionString_InsideAWorkerScope_UsesThatScopesTenant()
    {
        using var services = Build(
            options => options.GetConnectionString = t => $"Database=app_{t.TenantId}",
            tenant => tenant.UseInMemoryStore([Acme]));

        var connectionString = await services.GetRequiredService<ITenantScopeFactory<string>>()
            .RunInScopeAsync("acme", (scope, ct) =>
                scope.ServiceProvider.GetRequiredService<CurrentTenantConnectionString<string>>().GetAsync(ct).AsTask(), TestContext.Current.CancellationToken);

        connectionString.Should().Be("Database=app_acme");
    }

    [Fact]
    public async Task CurrentTenantConnectionString_WithoutACurrentTenant_Throws()
    {
        using var services = Build(options => options.GetConnectionString = _ => "x");
        var current = services.GetRequiredService<CurrentTenantConnectionString<string>>();

        current.Invoking(c => c.Get())
            .Should().Throw<TenantNotResolvedException>().WithMessage("*No tenant is current*");
        await current.Awaiting(c => c.GetAsync().AsTask())
            .Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public void Get_WhenOnlyTheAsyncDelegateIsConfigured_SaysToUseGetAsync()
    {
        using var services = Build(options => options.GetConnectionStringAsync = (_, _) => ValueTask.FromResult("x"));

        services.Invoking(s => Provider(s).Get(Acme))
            .Should().Throw<InvalidOperationException>().WithMessage("*Only GetConnectionStringAsync*GetAsync*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EmptyConnectionString_ThrowsNamingTheTenant(string? value)
    {
        using var services = Build(options =>
        {
            options.GetConnectionString = _ => value!;
            options.GetConnectionStringAsync = (_, _) => ValueTask.FromResult(value!);
        });

        services.Invoking(s => Provider(s).Get(Acme))
            .Should().Throw<InvalidOperationException>().WithMessage("*empty*'acme'*");
        await services.Awaiting(s => Provider(s).GetAsync(Acme).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty*'acme'*");
    }

    [Fact]
    public void Registration_WithoutADelegate_Throws()
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant => tenant.UseConnectionStrings(_ => { }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*GetConnectionString or GetConnectionStringAsync*");
    }

    [Fact]
    public void Registration_TwiceConfiguresOneOptionsInstanceAndOneProvider()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseConnectionStrings(options => options.GetConnectionString = _ => "first"));
        services.AddTenantry<string>(tenant => tenant.UseConnectionStrings(options => options.GetConnectionString = _ => "second"));
        using var provider = services.BuildServiceProvider();

        services.Should().ContainSingle(d => d.ServiceType == typeof(TenantConnectionStringOptions<string>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantConnectionStringProvider<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(CurrentTenantConnectionString<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        Provider(provider).Get(Acme).Should().Be("second");
        Provider(provider).Should().BeSameAs(provider.GetRequiredService<TenantConnectionStringProvider<string>>());
    }

    [Fact]
    public async Task Get_NullTenant_Throws()
    {
        using var services = Build(options => options.GetConnectionString = _ => "x");

        services.Invoking(s => Provider(s).Get(null!)).Should().Throw<ArgumentNullException>();
        await services.Awaiting(s => Provider(s).GetAsync(null!).AsTask()).Should().ThrowAsync<ArgumentNullException>();
    }

    private static ServiceProvider Build(
        Action<TenantConnectionStringOptions<string>> connectionStrings,
        Action<ITenantBuilder<string>>? configure = null)
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            configure?.Invoke(tenant);
            tenant.UseConnectionStrings(connectionStrings);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static ITenantConnectionStringProvider<string> Provider(IServiceProvider services) =>
        services.GetRequiredService<ITenantConnectionStringProvider<string>>();
}
