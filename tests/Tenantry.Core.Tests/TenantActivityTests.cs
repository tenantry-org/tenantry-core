using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

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

    private static ServiceProvider Build(Action<ITenantBuilder<string>> configure)
    {
        ServiceCollection services = new();
        services.AddTenantry(configure);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
