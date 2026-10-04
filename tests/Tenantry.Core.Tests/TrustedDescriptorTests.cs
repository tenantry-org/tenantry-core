using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>MakeCurrent</c> and <c>CreateScope</c> trust the descriptor they are given: they neither look it up in the store
/// nor ask <see cref="ITenantActivity{TKey}"/>. <c>RunInScopeAsync</c> takes an id and does both.
/// </summary>
public sealed class TrustedDescriptorTests : IAsyncLifetime
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme Corp" };
    private static readonly TenantDescriptor<string> Suspended = new() { TenantId = "suspended", Name = "Suspended Ltd" };
    private static readonly TenantDescriptor<string> Unknown = new() { TenantId = "unknown", Name = "Not in the store" };

    private ServiceProvider _services = null!;

    private ITenantScopeFactory<string> Scopes => _services.GetRequiredService<ITenantScopeFactory<string>>();

    private ITenantContextSetter<string> Ambient => _services.GetRequiredService<ITenantContextSetter<string>>();

    public ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([Acme, Suspended])
            .ValidateTenantActivity(t => t.TenantId != "suspended"));
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    public static TheoryData<string> Unchecked => ["unknown", "suspended"];

    [Theory]
    [MemberData(nameof(Unchecked))]
    public void MakeCurrent_MakesTheDescriptorCurrentWithoutCheckingIt(string tenantId)
    {
        var tenant = Descriptor(tenantId);

        using (Ambient.MakeCurrent(tenant))
        {
            Ambient.CurrentTenant.Should().BeSameAs(tenant);
        }
    }

    [Theory]
    [MemberData(nameof(Unchecked))]
    public async Task CreateScope_MakesTheDescriptorCurrentWithoutCheckingIt(string tenantId)
    {
        var tenant = Descriptor(tenantId);

        await using var scope = Scopes.CreateScope(tenant);

        scope.Tenant.Should().BeSameAs(tenant);
        scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenant.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task MakeCurrentAndCreateScope_KeepACopyThatDiffersFromTheStores()
    {
        TenantDescriptor<string> stale = new() { TenantId = "acme", Name = "Acme (old name)" };

        using (Ambient.MakeCurrent(stale))
        {
            Ambient.CurrentTenant.Should().BeSameAs(stale);
        }

        await using var scope = Scopes.CreateScope(stale);
        Ambient.CurrentTenant.Should().BeSameAs(stale);
    }

    [Fact]
    public async Task RunInScopeAsync_RefusesATenantTheStoreDoesNotHold()
    {
        var ran = false;

        await Scopes.Awaiting(s => s.RunInScopeAsync("unknown", (_, _) => { ran = true; return Task.CompletedTask; }))
            .Should().ThrowAsync<TenantNotFoundException>();
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_RefusesAnInactiveTenant()
    {
        var ran = false;

        await Scopes.Awaiting(s => s.RunInScopeAsync("suspended", (_, _) => { ran = true; return Task.CompletedTask; }))
            .Should().ThrowAsync<TenantInactiveException>();
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_RunsAsTheStoresCopy()
    {
        var tenant = await Scopes.RunInScopeAsync("acme", (scope, _) => Task.FromResult(scope.Tenant), TestContext.Current.CancellationToken);

        tenant.Should().BeSameAs(Acme);
    }

    private static TenantDescriptor<string> Descriptor(string tenantId) => tenantId == "suspended" ? Suspended : Unknown;
}
