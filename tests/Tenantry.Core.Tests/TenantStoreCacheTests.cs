using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tenantry.Internal;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>CacheTenants</c>: the tenants Tenantry reads through <see cref="ITenantStoreAccessor{TKey}"/>, by id and by
/// identifier, are kept for the configured duration, and <see cref="ITenantStoreCache{TKey}"/> removes them.
/// </summary>
public sealed class TenantStoreCacheTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    private readonly ManualTime _time = new();
    private readonly RecordingStore _store = new([Acme, new TenantDescriptor<string> { TenantId = "globex", Name = "Globex" }]);

    [Fact]
    public async Task ATenant_IsReadFromTheStoreOnce_UntilItExpires()
    {
        await using var provider = Build(o => o.Duration = TimeSpan.FromMinutes(2));
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        (await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken)).Should().BeSameAs(Acme);
        (await tenants.FindByIdentifierAsync("ACME", TestContext.Current.CancellationToken)).Should().BeSameAs(Acme);
        _time.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromTicks(1));
        (await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken)).Should().BeSameAs(Acme);
        (await tenants.FindByIdentifierAsync("ACME", TestContext.Current.CancellationToken)).Should().BeSameAs(Acme);

        _store.Reads.Should().Equal("id:acme", "identifier:ACME");
        _store.Scopes.Should().Be(2, "a cached tenant needs no scope");

        _time.Advance(TimeSpan.FromTicks(1));
        (await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken)).Should().BeSameAs(Acme);
        (await tenants.FindByIdentifierAsync("ACME", TestContext.Current.CancellationToken)).Should().BeSameAs(Acme);

        _store.Reads.Should().Equal("id:acme", "identifier:ACME", "id:acme", "identifier:ACME");
    }

    [Fact]
    public async Task ALookupThatFindsNoTenant_IsNotCached_AndTheListOfTenantsNeverIs()
    {
        await using var provider = Build();
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        (await tenants.GetTenantAsync("initech", TestContext.Current.CancellationToken)).Should().BeNull();
        (await tenants.GetTenantAsync("initech", TestContext.Current.CancellationToken)).Should().BeNull();
        (await tenants.FindByIdentifierAsync("initech", TestContext.Current.CancellationToken)).Should().BeNull();
        (await tenants.FindByIdentifierAsync("initech", TestContext.Current.CancellationToken)).Should().BeNull();
        (await tenants.GetAllTenantsAsync(TestContext.Current.CancellationToken)).Should().HaveCount(2);
        (await tenants.GetAllTenantsAsync(TestContext.Current.CancellationToken)).Should().HaveCount(2);

        _store.Reads.Should().Equal(
            "id:initech", "id:initech", "identifier:initech", "identifier:initech", "all", "all");
    }

    [Fact]
    public async Task Invalidate_RemovesTheTenant_ByItsIdAndEveryIdentifier_AndNoOther()
    {
        await using var provider = Build();
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();
        var cache = provider.GetRequiredService<ITenantStoreCache<string>>();

        foreach (var lookup in new[] { "acme", "ACME", "globex" })
        {
            await tenants.GetTenantAsync(lookup, TestContext.Current.CancellationToken);
            await tenants.FindByIdentifierAsync(lookup, TestContext.Current.CancellationToken);
        }

        _store.Reads.Clear();
        cache.Invalidate("acme");

        foreach (var lookup in new[] { "acme", "ACME", "globex" })
        {
            await tenants.GetTenantAsync(lookup, TestContext.Current.CancellationToken);
            await tenants.FindByIdentifierAsync(lookup, TestContext.Current.CancellationToken);
        }

        _store.Reads.Should().Equal("id:acme", "identifier:acme", "id:ACME", "identifier:ACME");
    }

    [Fact]
    public async Task InvalidateAll_RemovesEveryTenant()
    {
        await using var provider = Build();
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("globex", TestContext.Current.CancellationToken);
        provider.GetRequiredService<ITenantStoreCache<string>>().InvalidateAll();
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("globex", TestContext.Current.CancellationToken);

        _store.Reads.Should().Equal("id:acme", "identifier:globex", "id:acme", "identifier:globex");
    }

    [Fact]
    public async Task ATenantInvalidatedWhileTheStoreReadsIt_IsNotCached()
    {
        await using var provider = Build();
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();
        var cache = provider.GetRequiredService<ITenantStoreCache<string>>();

        foreach (var read in new Func<ValueTask<ITenantDescriptor<string>?>>[]
                 {
                     () => tenants.GetTenantAsync("acme"),
                     () => tenants.FindByIdentifierAsync("acme"),
                 })
        {
            _store.Reads.Clear();
            _store.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = read().AsTask();
            await _store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            cache.Invalidate("acme");
            _store.Gate.SetResult();
            (await reading).Should().BeSameAs(Acme, "the store's answer is still returned");

            _store.Gate = null;
            await read();
            _store.Reads.Should().HaveCount(2, "the answer read before the change is not kept");
        }
    }

    [Fact]
    public async Task WithoutCacheTenants_EveryLookupReadsTheStore_AndInvalidatingDoesNothing()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => _store));
        await using var provider = services.BuildServiceProvider();
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("acme", TestContext.Current.CancellationToken);

        _store.Reads.Should().HaveCount(4);

        // Always registered, so code that invalidates runs whether or not tenants are cached.
        var cache = provider.GetRequiredService<ITenantStoreCache<string>>();
        cache.Invalidate("acme");
        cache.InvalidateAll();
        cache.Invoking(c => c.Invalidate(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CacheTenants_BeforeTheStore_AndCalledAgain_ConfiguresOneCache()
    {
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(_time);
        services.AddTenantry<string>(tenant => tenant
            .CacheTenants()
            .UseStore(_ => _store)
            .CacheTenants(o => o.Duration = TimeSpan.FromSeconds(30)));
        await using var provider = services.BuildServiceProvider();
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        services.Should().ContainSingle(d => d.ServiceType == typeof(TenantStoreCacheOptions));
        services.Should().ContainSingle(d => d.ServiceType == typeof(ITenantStoreCache<string>));
        provider.GetRequiredService<ITenantStoreCache<string>>().Should().BeSameAs(provider.GetRequiredService<TenantStoreCache<string>>());
        provider.GetRequiredService<TenantStoreCacheOptions>().Duration.Should().Be(TimeSpan.FromSeconds(30));

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(30));
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);

        _store.Reads.Should().HaveCount(2);
    }

    [Fact]
    public void TheDefaultDuration_IsFiveMinutes() =>
        new TenantStoreCacheOptions().Duration.Should().Be(TimeSpan.FromMinutes(5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADurationThatIsNotPositive_Throws(int seconds)
    {
        ServiceCollection services = new();

        var act = () => services.AddTenantry<string>(tenant => tenant.CacheTenants(o => o.Duration = TimeSpan.FromSeconds(seconds)));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("options.Duration");
    }

    [Fact]
    public async Task AnEndlessDuration_NeverExpires()
    {
        await using var provider = Build(o => o.Duration = TimeSpan.MaxValue);
        var tenants = provider.GetRequiredService<ITenantStoreAccessor<string>>();

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromDays(3650));
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);

        _store.Reads.Should().ContainSingle();
    }

    [Fact]
    public void AFullCache_StopsGrowing_UntilItsEntriesExpire()
    {
        TenantStoreCache<string> cache = new(new TenantStoreCacheOptions { Duration = TimeSpan.FromMinutes(1) }, _time);

        for (var i = 0; i < TenantStoreCache<string>.MaxEntries; i++)
        {
            cache.SetByIdentifier($"acme-{i}", Acme, cache.Generation);
        }

        cache.SetByIdentifier("one-more", Acme, cache.Generation);
        cache.TryGetByIdentifier("one-more", out _).Should().BeFalse("the cache is full of entries that have not expired");
        cache.TryGetByIdentifier("acme-0", out _).Should().BeTrue();

        // Once they expire, the expired entries are pruned to make room.
        _time.Advance(TimeSpan.FromMinutes(1));
        cache.SetByIdentifier("one-more", Acme, cache.Generation);
        cache.TryGetByIdentifier("one-more", out var tenant).Should().BeTrue();
        tenant.Should().BeSameAs(Acme);

        // An entry read after it expires is not returned.
        cache.TryGetByIdentifier("acme-0", out _).Should().BeFalse();
    }

    private ServiceProvider Build(Action<TenantStoreCacheOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(_time);
        services.AddTenantry<string>(tenant => tenant
            .UseStore(_ =>
            {
                _store.Scopes++;
                return _store;
            })
            .CacheTenants(configure));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // Matches identifiers without regard to case, as a database's default collation would.
    private sealed class RecordingStore(IEnumerable<TenantDescriptor<string>> tenants) : ITenantStore<string>
    {
        private readonly List<TenantDescriptor<string>> _tenants = [.. tenants];

        public List<string> Reads { get; } = [];

        public int Scopes { get; set; }

        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? Gate { get; set; }

        public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Reads.Add($"id:{tenantId}");
            await WaitAsync();
            return _tenants.Find(t => string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        }

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
        {
            Reads.Add("all");
            return ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>(_tenants);
        }

        public async ValueTask<ITenantDescriptor<string>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
        {
            Reads.Add($"identifier:{identifier}");
            await WaitAsync();
            return _tenants.Find(t => string.Equals(t.TenantId, identifier, StringComparison.OrdinalIgnoreCase));
        }

        private async Task WaitAsync()
        {
            if (Gate is { } gate)
            {
                Entered.TrySetResult();
                await gate.Task;
                Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
