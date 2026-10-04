using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tenantry.Core.Tests;

/// <summary>
/// A desktop app calls Tenantry on its UI thread, which has a synchronization context. The work it passes to
/// <c>RunInScopeAsync</c> runs there, and Tenantry's own continuations do not, so an app that blocks on a read there
/// does not deadlock.
/// </summary>
public sealed class SynchronizationContextTests : IAsyncLifetime, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    private readonly SingleThreadContext _ui = new();
    private ServiceProvider _services = null!;

    public ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant
            .UseStore(_ => new SlowStore())
            .ValidateTenantActivity(async (t, ct) =>
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                return t.TenantId == "acme";
            })
            .ValidateTenantActivity(async (_, ct) =>
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                return true;
            })
            .UseConnectionStrings(options => options.GetConnectionStringAsync = async (t, ct) =>
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                return $"Data Source={t.TenantId}";
            }));
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public async Task RunInScopeAsync_RunsTheWorkOnTheCallersContext()
    {
        var scopes = _services.GetRequiredService<ITenantScopeFactory<string>>();

        var onContext = await _ui.RunAsync(() =>
                scopes.RunInScopeAsync("acme", (scope, _) => Task.FromResult(_ui.IsCurrent && scope.Tenant.TenantId == "acme")))
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        onContext.Should().BeTrue("the work is the caller's, after the lookup and the activity checks");
    }

    [Fact]
    public async Task BlockingOnTheLookupOnTheContextsThread_DoesNotDeadlock()
    {
        var lookup = _services.GetRequiredService<ITenantLookup<string>>();

        var found = await _ui.Run(() => (
                lookup.GetTenantAsync("acme").AsTask().GetAwaiter().GetResult(),
                lookup.FindByIdentifierAsync("acme").AsTask().GetAwaiter().GetResult(),
                lookup.GetAllTenantsAsync().AsTask().GetAwaiter().GetResult()))
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        found.Item1.Should().BeSameAs(Acme);
        found.Item2.Should().BeSameAs(Acme);
        found.Item3.Should().Equal(Acme);
    }

    [Fact]
    public async Task BlockingOnTheActivityChecksOnTheContextsThread_DoesNotDeadlock()
    {
        var activity = _services.GetRequiredService<ITenantActivity<string>>();

        var active = await _ui.Run(() => activity.IsActiveAsync(Acme).AsTask().GetAwaiter().GetResult())
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        active.Should().BeTrue();
    }

    [Fact]
    public async Task BlockingOnAConnectionStringOnTheContextsThread_DoesNotDeadlock()
    {
        var connectionStrings = _services.GetRequiredService<ITenantConnectionStringProvider<string>>();

        var connectionString = await _ui.Run(() => connectionStrings.GetAsync(Acme).AsTask().GetAwaiter().GetResult())
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        connectionString.Should().Be("Data Source=acme");
    }

    /// <summary>A store that answers later, on the thread pool, as a database-backed one does.</summary>
    private sealed class SlowStore : ITenantStore<string>
    {
        public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return tenantId == "acme" ? Acme : null;
        }

        public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return [Acme];
        }

        public async ValueTask<ITenantDescriptor<string>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return identifier == "acme" ? Acme : null;
        }
    }
}
