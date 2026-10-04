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

        using (services.GetRequiredService<ITenantContextSetter<string>>().MakeCurrent(Acme))
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
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantConnectionStringProvider<string>) && !d.IsKeyedService)
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(CurrentTenantConnectionString<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        Provider(provider).Get(Acme).Should().Be("second");
    }

    [Fact]
    public async Task AProviderFromDI_IsBuiltFromTheApplicationsServices()
    {
        ServiceCollection services = new();
        services.AddSingleton(new Vault("secret"));
        services.AddTenantry<string>(tenant => tenant
            .UseConnectionStrings(_ => new VaultProvider(new Vault("replaced")))
            .UseConnectionStrings(sp => new VaultProvider(sp.GetRequiredService<Vault>())));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Provider(provider).Get(Acme).Should().Be("secret acme");
        Provider(provider).CanGetSynchronously.Should().BeFalse();
        await provider.GetRequiredService<CurrentTenantConnectionString<string>>()
            .Awaiting(c => c.GetAsync(TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public void TheDelegates_AfterAProviderFromDI_Throw()
    {
        ServiceCollection services = new();

        services.Invoking(s => s.AddTenantry<string>(tenant => tenant
                .UseConnectionStrings(_ => new VaultProvider(new Vault("first")))
                .UseConnectionStrings(options => options.GetConnectionString = _ => "second")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("UseConnectionStrings(options => …) cannot be combined with UseConnectionStrings(sp => …)*use one*");
    }

    [Fact]
    public void TheDelegates_AfterAProviderTheApplicationRegistered_Throw()
    {
        ServiceCollection services = new();
        services.AddSingleton<ITenantConnectionStringProvider<string>>(new VaultProvider(new Vault("own")));

        services.Invoking(s => s.AddTenantry<string>(tenant => tenant
                .UseConnectionStrings(options => options.GetConnectionString = _ => "second")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*ITenantConnectionStringProvider<String> registered before AddTenantry*use one*");
    }

    [Fact]
    public void AProviderFromDI_AfterTheDelegates_Throws()
    {
        ServiceCollection services = new();

        services.Invoking(s => s.AddTenantry<string>(tenant => tenant
                .UseConnectionStrings(options => options.GetConnectionString = _ => "first")
                .UseConnectionStrings(_ => new VaultProvider(new Vault("second")))))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("UseConnectionStrings(sp => …) cannot be combined with UseConnectionStrings(options => …)*use one*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decorators_WrapTheProvider_WhicheverIsRegisteredFirst(bool decorateFirst)
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant =>
        {
            if (decorateFirst)
            {
                tenant.DecorateConnectionStrings((_, inner) => new Suffix(inner, " first"));
                tenant.DecorateConnectionStrings((_, inner) => new Suffix(inner, " second"));
            }

            tenant.UseConnectionStrings(options => options.GetConnectionString = t => t.TenantId);

            if (!decorateFirst)
            {
                tenant.DecorateConnectionStrings((_, inner) => new Suffix(inner, " first"));
                tenant.DecorateConnectionStrings((_, inner) => new Suffix(inner, " second"));
            }
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Provider(provider).Get(Acme).Should().Be("acme first second");
        Provider(provider).CanGetSynchronously.Should().BeTrue();
    }

    [Fact]
    public void Decorators_WrapAProviderTheApplicationRegisteredItself()
    {
        ServiceCollection services = new();
        services.AddSingleton<ITenantConnectionStringProvider<string>>(new VaultProvider(new Vault("own")));
        services.AddTenantry<string>(tenant => tenant.DecorateConnectionStrings((_, inner) => new Suffix(inner, " decorated")));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Provider(provider).Get(Acme).Should().Be("own acme decorated");
    }

    [Fact]
    public void Decorators_WithoutConnectionStrings_ThrowWithGuidance()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.DecorateConnectionStrings((_, inner) => inner));
        using var provider = services.BuildServiceProvider();

        provider.Invoking(Provider).Should().Throw<InvalidOperationException>().WithMessage("*UseConnectionStrings*");
    }

    [Fact]
    public void CanGetSynchronously_FollowsTheDelegates()
    {
        using var asyncOnly = Build(options => options.GetConnectionStringAsync = (_, _) => ValueTask.FromResult("x"));
        using var sync = Build(options => options.GetConnectionString = _ => "x");

        Provider(asyncOnly).CanGetSynchronously.Should().BeFalse();
        Provider(sync).CanGetSynchronously.Should().BeTrue();
    }

    private sealed record Vault(string Secret);

    // Reads synchronously too, but says it cannot, as a vault client might.
    private sealed class VaultProvider(Vault vault) : ITenantConnectionStringProvider<string>
    {
        public bool CanGetSynchronously => false;

        public string Get(ITenantDescriptor<string> tenant) => $"{vault.Secret} {tenant.TenantId}";

        public ValueTask<string> GetAsync(ITenantDescriptor<string> tenant, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Get(tenant));
    }

    private sealed class Suffix(ITenantConnectionStringProvider<string> inner, string suffix) : ITenantConnectionStringProvider<string>
    {
        public bool CanGetSynchronously => inner.CanGetSynchronously;

        public string Get(ITenantDescriptor<string> tenant) => inner.Get(tenant) + suffix;

        public async ValueTask<string> GetAsync(ITenantDescriptor<string> tenant, CancellationToken cancellationToken = default) =>
            await inner.GetAsync(tenant, cancellationToken) + suffix;
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
