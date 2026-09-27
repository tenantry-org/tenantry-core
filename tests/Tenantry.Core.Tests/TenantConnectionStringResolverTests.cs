using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Core.Exceptions;
using Tenantry.Core.Extensions;

namespace Tenantry.Core.Tests;

public sealed class TenantConnectionStringResolverTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    [Fact]
    public void Resolve_UsesTheCurrentTenant()
    {
        using var services = Build(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");

        using (services.GetRequiredService<ITenantScope<string>>().BeginScope(Acme))
        {
            Resolver(services).Resolve().Should().Be("Database=app_acme");
        }
    }

    [Fact]
    public void Resolve_WithAnExplicitTenant_NeedsNoCurrentTenant()
    {
        using var services = Build(options => options.GetConnectionString = t => $"Database=app_{t.TenantId}");

        Resolver(services).Resolve(Acme).Should().Be("Database=app_acme");
    }

    [Fact]
    public async Task ResolveAsync_PrefersTheAsyncDelegate()
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

        (await Resolver(services).ResolveAsync(Acme)).Should().Be("async acme");
        Resolver(services).Resolve(Acme).Should().Be("sync", "the synchronous overload uses the synchronous delegate");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToTheSyncDelegate()
    {
        using var services = Build(options => options.GetConnectionString = t => $"sync {t.TenantId}");

        using (services.GetRequiredService<ITenantScope<string>>().BeginScope(Acme))
        {
            (await Resolver(services).ResolveAsync()).Should().Be("sync acme");
        }
    }

    [Fact]
    public async Task ResolveAsync_PassesTheCancellationToken()
    {
        CancellationToken received = default;
        using CancellationTokenSource cts = new();
        using var services = Build(options => options.GetConnectionStringAsync = (_, ct) =>
        {
            received = ct;
            return ValueTask.FromResult("x");
        });

        await Resolver(services).ResolveAsync(Acme, cts.Token);

        received.Should().Be(cts.Token);
    }

    [Fact]
    public async Task ResolveAsync_InsideAWorkerScope_UsesThatScopesTenant()
    {
        using var services = Build(
            options => options.GetConnectionString = t => $"Database=app_{t.TenantId}",
            tenant => tenant.UseInMemoryStore([Acme]));

        var connectionString = await services.GetRequiredService<ITenantScopeFactory<string>>()
            .RunInScopeAsync("acme", (scope, ct) =>
                scope.ServiceProvider.GetRequiredService<ITenantConnectionStringResolver<string>>().ResolveAsync(ct).AsTask());

        connectionString.Should().Be("Database=app_acme");
    }

    [Fact]
    public async Task WithoutACurrentTenant_TheCurrentTenantOverloadsThrow()
    {
        using var services = Build(options => options.GetConnectionString = _ => "x");

        services.Invoking(s => Resolver(s).Resolve())
            .Should().Throw<TenantNotResolvedException>().WithMessage("*No tenant is current*");
        await services.Awaiting(s => Resolver(s).ResolveAsync().AsTask())
            .Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public void Resolve_WhenOnlyTheAsyncDelegateIsConfigured_SaysToUseResolveAsync()
    {
        using var services = Build(options => options.GetConnectionStringAsync = (_, _) => ValueTask.FromResult("x"));

        services.Invoking(s => Resolver(s).Resolve(Acme))
            .Should().Throw<InvalidOperationException>().WithMessage("*Only GetConnectionStringAsync*ResolveAsync*");
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

        services.Invoking(s => Resolver(s).Resolve(Acme))
            .Should().Throw<InvalidOperationException>().WithMessage("*empty*'acme'*");
        await services.Awaiting(s => Resolver(s).ResolveAsync(Acme).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty*'acme'*");
    }

    [Fact]
    public void Registration_WithoutADelegate_Throws()
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantConnectionStrings<string>(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*GetConnectionString or GetConnectionStringAsync*");
    }

    [Fact]
    public void Registration_TwiceConfiguresOneOptionsInstanceAndOneResolver()
    {
        ServiceCollection services = new();
        services.AddTenantConnectionStrings<string>(options => options.GetConnectionString = _ => "first");
        services.AddTenantConnectionStrings<string>(options => options.GetConnectionString = _ => "second");
        using var provider = services.BuildServiceProvider();

        services.Should().ContainSingle(d => d.ServiceType == typeof(TenantConnectionStringOptions<string>));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantConnectionStringResolver<string>))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        Resolver(provider).Resolve(Acme).Should().Be("second");
        Resolver(provider).Should().BeSameAs(provider.GetRequiredService<TenantConnectionStringResolver<string>>());
    }

    [Fact]
    public void Resolve_NullTenant_Throws()
    {
        using var services = Build(options => options.GetConnectionString = _ => "x");

        services.Invoking(s => Resolver(s).Resolve(null!)).Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider Build(
        Action<TenantConnectionStringOptions<string>> connectionStrings,
        Action<ITenantBuilder<string>>? configure = null)
    {
        ServiceCollection services = new();
        services.AddTenantryCore<string>(tenant =>
        {
            configure?.Invoke(tenant);
            tenant.UseConnectionStrings(connectionStrings);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static ITenantConnectionStringResolver<string> Resolver(IServiceProvider services) =>
        services.GetRequiredService<ITenantConnectionStringResolver<string>>();
}
