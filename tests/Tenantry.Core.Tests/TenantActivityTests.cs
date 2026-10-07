using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>ValidateTenantActivity</c>: one check, consulted by <c>RunInScopeAsync</c> and by anything that asks
/// <see cref="ITenantActivity{TKey}"/>, that stops work for suspended tenants.
/// </summary>
public sealed class TenantActivityTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme Corp" };
    private static readonly TenantDescriptor<string> Suspended = new() { TenantId = "suspended", Name = "Suspended Ltd" };

    [Fact]
    public async Task WithoutValidators_EveryTenantIsActive()
    {
        await using var services = Build(tenant => tenant.UseInMemoryStore([Acme]));
        var activity = services.GetRequiredService<ITenantActivity<string>>();

        (await activity.IsActiveAsync(Acme, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task EveryValidatorMustAllowTheTenant()
    {
        await using var services = Build(tenant => tenant
            .UseInMemoryStore([Acme, Suspended])
            .ValidateTenantActivity(_ => true)
            .ValidateTenantActivity(t => t.TenantId != "suspended"));
        var activity = services.GetRequiredService<ITenantActivity<string>>();

        (await activity.IsActiveAsync(Acme, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await activity.IsActiveAsync(Suspended, TestContext.Current.CancellationToken)).Should().BeFalse();
        await activity.Awaiting(a => a.ThrowIfInactiveAsync(Suspended, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TenantInactiveException>().Where(e => (string)e.TenantId == "suspended");
    }

    [Fact]
    public async Task RunInScopeAsync_RefusesAnInactiveTenant_WithoutRunningTheWork()
    {
        await using var services = Build(tenant => tenant
            .UseInMemoryStore([Acme, Suspended])
            .ValidateTenantActivity((t, _) => ValueTask.FromResult(t.TenantId != "suspended")));
        var scopes = services.GetRequiredService<ITenantScopeFactory<string>>();
        var ran = false;

        await scopes.Awaiting(s => s.RunInScopeAsync("suspended", (_, _) => { ran = true; return Task.CompletedTask; }))
            .Should().ThrowAsync<TenantInactiveException>();
        ran.Should().BeFalse();

        (await scopes.RunInScopeAsync("acme", (scope, _) => Task.FromResult(scope.Tenant.TenantId), TestContext.Current.CancellationToken)).Should().Be("acme");
    }

    [Fact]
    public async Task TheExceptionCarriesTheIdAsTheKeyType()
    {
        TenantDescriptor<Guid> suspended = new() { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Suspended Ltd" };
        ServiceCollection services = new();
        services.AddTenantry<Guid>(tenant => tenant.UseInMemoryStore([suspended]).ValidateTenantActivity(_ => false));
        await using var provider = services.BuildServiceProvider();
        var activity = provider.GetRequiredService<ITenantActivity<Guid>>();

        await activity.Awaiting(a => a.ThrowIfInactiveAsync(suspended, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TenantInactiveException>().Where(e => e.TenantId is Guid && (Guid)e.TenantId == suspended.TenantId);
    }

    [Fact]
    public async Task AValidatorType_IsASingleton_ConsultedWithTheOthers()
    {
        await using var services = Build(tenant => tenant
            .UseInMemoryStore([Acme, Suspended])
            .ValidateTenantActivity(_ => true)
            .ValidateTenantActivity<NotSuspended>()
            .ValidateTenantActivity<NotSuspended>());
        var activity = services.GetRequiredService<ITenantActivity<string>>();

        (await activity.IsActiveAsync(Acme, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await activity.IsActiveAsync(Suspended, TestContext.Current.CancellationToken)).Should().BeFalse();
        services.GetServices<ITenantActivityValidator<string>>().OfType<NotSuspended>().Should().ContainSingle("a type added twice is registered once");
        services.GetRequiredService<ITenantActivity<string>>().Should().BeSameAs(activity);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void AValidatorThatIsNotASingleton_IsRefused_NamingIt(ServiceLifetime lifetime)
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]));
        services.Add(new ServiceDescriptor(typeof(ITenantActivityValidator<string>), typeof(NotSuspended), lifetime));
        using var provider = services.BuildServiceProvider();

        provider.Invoking(p => p.GetRequiredService<ITenantActivity<string>>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*ITenantActivityValidator<String> NotSuspended is registered as {lifetime.ToString().ToLowerInvariant()}*ValidateTenantActivity<TValidator>()*");
    }

    private sealed class NotSuspended : ITenantActivityValidator<string>
    {
        public ValueTask<bool> IsActiveAsync(ITenantDescriptor<string> tenant, CancellationToken cancellationToken) =>
            ValueTask.FromResult(tenant.TenantId != "suspended");
    }

    private static ServiceProvider Build(Action<ITenantBuilder<string>> configure)
    {
        ServiceCollection services = new();
        services.AddTenantry(configure);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
