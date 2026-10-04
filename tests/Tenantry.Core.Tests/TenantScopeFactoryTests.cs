using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// Worker scopes against the real AsyncLocal tenant scope. The first seven tests are the cases that showed
/// the old Pro <c>CreateScopeAsync</c> and async disposal leaving callers on the wrong tenant.
/// </summary>
public sealed class TenantScopeFactoryTests : IAsyncLifetime
{
    private readonly YieldingStore _store = new();
    private ServiceProvider _services = null!;

    private ITenantScopeFactory<string> Scopes => _services.GetRequiredService<ITenantScopeFactory<string>>();

    private ITenantContextSetter<string> Ambient => _services.GetRequiredService<ITenantContextSetter<string>>();

    public ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => _store));
        services.AddScoped<DisposalProbe>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    // [1] The tenant is active for the code that created the scope, and for services resolved from it.
    [Fact]
    public void CreateScope_ActivatesTheTenantForTheCallerAndTheScopesServices()
    {
        using var scope = Scopes.CreateScope(Tenant("acme"));

        Ambient.CurrentTenantId.Should().Be("acme");
        scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenantId.Should().Be("acme");
        scope.Tenant.TenantId.Should().Be("acme");
    }

    // [2] await using restores a tenant that was already active.
    [Fact]
    public async Task CreateScope_WithAwaitUsing_RestoresThePreviousTenant()
    {
        using (Ambient.MakeCurrent(Tenant("old")))
        {
            await using (Scopes.CreateScope(Tenant("acme")))
            {
                await Task.Yield();
                Ambient.CurrentTenantId.Should().Be("acme");
            }

            Ambient.CurrentTenantId.Should().Be("old");
        }
    }

    // [3] await using restores "no tenant". The old async DisposeAsync left the tenant active here.
    [Fact]
    public async Task CreateScope_WithAwaitUsing_RestoresNoTenant()
    {
        await using (Scopes.CreateScope(Tenant("acme")))
        {
            await Task.Yield();
        }

        Ambient.HasTenant.Should().BeFalse();
    }

    // [4] Synchronous using behaves the same way.
    [Fact]
    public void CreateScope_WithUsing_RestoresThePreviousTenant()
    {
        using (Ambient.MakeCurrent(Tenant("old")))
        {
            using (Scopes.CreateScope(Tenant("acme")))
            {
                Ambient.CurrentTenantId.Should().Be("acme");
            }

            Ambient.CurrentTenantId.Should().Be("old");
        }

        Ambient.HasTenant.Should().BeFalse();
    }

    // [5] Nested scopes restore each outer tenant in turn.
    [Fact]
    public async Task NestedScopes_WithAwaitUsing_RestoreEachOuterTenantInTurn()
    {
        await using (Scopes.CreateScope(Tenant("outer")))
        {
            await using (Scopes.CreateScope(Tenant("inner")))
            {
                await Task.Yield();
                Ambient.CurrentTenantId.Should().Be("inner");
            }

            Ambient.CurrentTenantId.Should().Be("outer");
        }

        Ambient.HasTenant.Should().BeFalse();
    }

    // [6] A sweep over every tenant runs each as itself and leaves no tenant behind.
    [Fact]
    public async Task SweepLoop_RunsEachTenantAsItselfAndLeavesNoTenantBehind()
    {
        List<string?> seen = [];

        foreach (var id in new[] { "t1", "t2", "t3" })
        {
            await using var scope = Scopes.CreateScope(Tenant(id));
            await Task.Yield();
            seen.Add(scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenantId);
        }

        seen.Should().Equal("t1", "t2", "t3");
        Ambient.HasTenant.Should().BeFalse();
    }

    // [7] Concurrent scopes each see only their own tenant, and the caller is unaffected.
    [Fact]
    public async Task ConcurrentScopes_EachSeeOnlyTheirOwnTenant()
    {
        async Task<string> Worker(string id, int delay)
        {
            await using var scope = Scopes.CreateScope(Tenant(id));
            await Task.Delay(delay);
            var ambient = Ambient.CurrentTenantId;
            await Task.Delay(delay);
            var resolved = scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenantId;
            return $"{id}:{ambient}/{resolved}";
        }

        var results = await Task.WhenAll(Worker("c1", 30), Worker("c2", 10));

        results.Should().Equal("c1:c1/c1", "c2:c2/c2");
        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_RunsTheWorkAsTheStoredTenantWithoutChangingTheCaller()
    {
        _store.Yield = true;

        using (Ambient.MakeCurrent(Tenant("old")))
        {
            await Scopes.RunInScopeAsync("acme", async (scope, _) =>
            {
                await Task.Yield();
                Ambient.CurrentTenantId.Should().Be("acme");
                scope.ServiceProvider.GetRequiredService<ITenantContext<string>>().CurrentTenantId.Should().Be("acme");
                scope.Tenant.Name.Should().Be("Stored acme", "the descriptor comes from the store");
            }, TestContext.Current.CancellationToken);

            Ambient.CurrentTenantId.Should().Be("old");
        }

        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_ConcurrentRuns_EachSeeOnlyTheirOwnTenant()
    {
        _store.Yield = true;

        Task<string> Run(string id, int delay) =>
            Scopes.RunInScopeAsync(id, async (scope, ct) =>
            {
                await Task.Delay(delay, ct);
                return $"{id}:{Ambient.CurrentTenantId}/{scope.Tenant.TenantId}";
            });

        var results = await Task.WhenAll(Run("d1", 30), Run("d2", 10));

        results.Should().Equal("d1:d1/d1", "d2:d2/d2");
        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_ReturnsTheWorksResult()
    {
        var name = await Scopes.RunInScopeAsync("acme", (scope, _) => Task.FromResult(scope.Tenant.Name), TestContext.Current.CancellationToken);

        name.Should().Be("Stored acme");
    }

    [Fact]
    public async Task RunInScopeAsync_UnknownTenant_ThrowsWithoutRunningTheWork()
    {
        var ran = false;

        var act = () => Scopes.RunInScopeAsync("missing", (_, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        var thrown = await act.Should().ThrowAsync<TenantNotFoundException>();
        thrown.WithMessage("*'missing'*").Which.TenantId.Should().Be("missing");
        thrown.Which.Should().BeAssignableTo<TenantNotResolvedException>("existing handlers of that exception still catch it");
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_EmptyId_ThrowsWithoutLookingUpTheTenant()
    {
        var act = () => Scopes.RunInScopeAsync("", (_, _) => Task.CompletedTask);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("tenantId");
        _store.Lookups.Should().Be(0);
    }

    [Fact]
    public void CreateScope_TenantWithTheKeyTypesDefaultId_ThrowsBeforeCreatingTheScope()
    {
        var act = () => Scopes.CreateScope(new TenantDescriptor<string> { TenantId = "", Name = "Unnamed" });

        act.Should().Throw<ArgumentException>().WithMessage("*reserves*").WithParameterName("tenant");
        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task CreateScope_ValueTypeKeyWithDefaultId_Throws()
    {
        ServiceCollection services = new();
        services.AddTenantry<Guid>(tenant => tenant.UseInMemoryStore([]));
        await using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<ITenantScopeFactory<Guid>>();

        var create = () => scopes.CreateScope(new TenantDescriptor<Guid> { TenantId = Guid.Empty, Name = "Empty" });
        var run = () => scopes.RunInScopeAsync(Guid.Empty, (_, _) => Task.CompletedTask);

        create.Should().Throw<ArgumentException>();
        await run.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunInScopeAsync_WhenTheWorkThrows_PropagatesAndDisposesTheScope()
    {
        DisposalProbe? probe = null;

        var act = () => Scopes.RunInScopeAsync("acme", (scope, _) =>
        {
            probe = scope.ServiceProvider.GetRequiredService<DisposalProbe>();
            throw new InvalidOperationException("boom");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        probe!.Disposed.Should().BeTrue();
        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task RunInScopeAsync_CancelledToken_ThrowsBeforeLookingUpTheTenant()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        var token = cts.Token;

        var act = () => Scopes.RunInScopeAsync("acme", (_, _) => Task.CompletedTask, token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Lookups.Should().Be(0);
    }

    [Fact]
    public async Task RunInScopeAsync_PassesTheTokenToTheStoreAndTheWork()
    {
        using CancellationTokenSource cts = new();
        CancellationToken received = CancellationToken.None;

        await Scopes.RunInScopeAsync("acme", (_, ct) =>
        {
            received = ct;
            return Task.CompletedTask;
        }, cts.Token);

        received.Should().Be(cts.Token);
        _store.LastToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task DisposeAsync_ScopedServicesSeeTheTenantWhileTheyAreDisposed()
    {
        DisposalProbe probe;

        await using (var scope = Scopes.CreateScope(Tenant("acme")))
        {
            probe = scope.ServiceProvider.GetRequiredService<DisposalProbe>();
        }

        probe.TenantAtDisposal.Should().Be("acme", "services are disposed before the tenant is restored");
        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ScopedServicesSeeTheTenantWhileTheyAreDisposed()
    {
        DisposalProbe probe;

        using (var scope = Scopes.CreateScope(Tenant("acme")))
        {
            probe = scope.ServiceProvider.GetRequiredService<DisposalProbe>();
        }

        probe.TenantAtDisposal.Should().Be("acme");
        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task EachScope_HasItsOwnScopedServices()
    {
        await using var first = Scopes.CreateScope(Tenant("acme"));
        await using var second = Scopes.CreateScope(Tenant("globex"));

        first.ServiceProvider.GetRequiredService<DisposalProbe>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<DisposalProbe>());
    }

    [Fact]
    public async Task DisposingTwice_IsHarmless()
    {
        using (Ambient.MakeCurrent(Tenant("old")))
        {
            var scope = Scopes.CreateScope(Tenant("acme"));
            await scope.DisposeAsync();

            using (Ambient.MakeCurrent(Tenant("later")))
            {
                await scope.DisposeAsync();
                scope.Dispose();

                Ambient.CurrentTenantId.Should().Be("later");
            }

            Ambient.CurrentTenantId.Should().Be("old");
        }
    }

    [Fact]
    public async Task Scope_DisposedByAChildTaskFirst_IsStillRestoredWhenTheCallerDisposesIt()
    {
        var scope = Scopes.CreateScope(Tenant("acme"));

        await Task.Run(() => scope.DisposeAsync().AsTask(), TestContext.Current.CancellationToken);
        await scope.DisposeAsync();

        Ambient.HasTenant.Should().BeFalse();
    }

    [Fact]
    public void CreateScope_NullTenant_Throws()
    {
        var act = () => Scopes.CreateScope(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static TenantDescriptor<string> Tenant(string id) => new() { TenantId = id, Name = id };

    /// <summary>A store whose lookups can complete asynchronously, like a database-backed one.</summary>
    private sealed class YieldingStore : ITenantStore<string>
    {
        private int _lookups;

        public bool Yield { get; set; }

        public int Lookups => _lookups;

        public CancellationToken LastToken { get; private set; }

        public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _lookups);
            LastToken = cancellationToken;

            if (Yield)
            {
                await Task.Yield();
            }

            return tenantId == "missing" ? null : new TenantDescriptor<string> { TenantId = tenantId, Name = $"Stored {tenantId}" };
        }

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([]);
    }

    /// <summary>A scoped service that records the tenant it sees while being disposed.</summary>
    private sealed class DisposalProbe(ITenantContext<string> context) : IDisposable, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public string? TenantAtDisposal { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            TenantAtDisposal = context.CurrentTenantId;
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Disposed = true;
            TenantAtDisposal = context.CurrentTenantId;
        }
    }
}
