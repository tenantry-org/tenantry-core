using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tenantry.Internal;

namespace Tenantry.Core.Tests;

/// <summary>
/// <c>CacheTenants</c>: the tenants Tenantry reads through <see cref="ITenantLookup{TKey}"/>, by id and by
/// identifier, are kept for the configured duration, and <see cref="ITenantInvalidator{TKey}"/> removes them.
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
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();

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
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();

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
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();
        var cache = provider.GetRequiredService<ITenantInvalidator<string>>();

        foreach (var lookup in new[] { "acme", "ACME", "globex" })
        {
            await tenants.GetTenantAsync(lookup, TestContext.Current.CancellationToken);
            await tenants.FindByIdentifierAsync(lookup, TestContext.Current.CancellationToken);
        }

        _store.Reads.Clear();
        await cache.InvalidateAsync("acme", TestContext.Current.CancellationToken);

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
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("globex", TestContext.Current.CancellationToken);
        await provider.GetRequiredService<ITenantInvalidator<string>>().InvalidateAllAsync(TestContext.Current.CancellationToken);
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("globex", TestContext.Current.CancellationToken);

        _store.Reads.Should().Equal("id:acme", "identifier:globex", "id:acme", "identifier:globex");
    }

    [Fact]
    public async Task ATenantInvalidatedWhileTheStoreReadsIt_IsNotCached()
    {
        await using var provider = Build();
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();
        var cache = provider.GetRequiredService<ITenantInvalidator<string>>();

        foreach (var read in new[]
                 {
                     () => tenants.GetTenantAsync("acme"),
                     () => tenants.FindByIdentifierAsync("acme"),
                 })
        {
            _store.Reads.Clear();
            _store.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = read().AsTask();
            await _store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            await cache.InvalidateAsync("acme", TestContext.Current.CancellationToken);
            _store.Gate.SetResult();
            (await reading).Should().BeSameAs(Acme, "the store's answer is still returned");

            _store.Gate = null;
            await read();
            _store.Reads.Should().HaveCount(2, "the answer read before the change is not kept");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AReadThatEndsAfterAnInvalidation_NeverReplacesWhatAReaderCachedSince(bool byIdentifier)
    {
        VersionedStore store = new();
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => store).CacheTenants());
        await using var provider = services.BuildServiceProvider();
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();
        var invalidator = provider.GetRequiredService<ITenantInvalidator<string>>();
        ValueTask<ITenantDescriptor<string>?> Read() =>
            byIdentifier ? tenants.FindByIdentifierAsync("acme") : tenants.GetTenantAsync("acme");

        // The first read is held open across an invalidation; a second reader looks the tenant up meanwhile.
        store.HoldNextRead();
        var first = Read().AsTask();
        await store.Held.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        store.Version = 2;
        await invalidator.InvalidateAsync("acme", TestContext.Current.CancellationToken);
        (await Read())!.Name.Should().Be("v2");
        store.Release();
        (await first)!.Name.Should().Be("v1", "the held read returns what the store answered");

        (await Read())!.Name.Should().Be("v2");
        store.Reads.Should().Be(2, "the second reader's entry is still cached, and the held read's was never published");
    }

    [Fact]
    public async Task UnderConcurrentReadsAndInvalidations_NoLookupAfterAnInvalidation_ReturnsWhatItRemoved()
    {
        VersionedStore store = new() { Yield = true };
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => store).CacheTenants());
        await using var provider = services.BuildServiceProvider();
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();
        var invalidator = provider.GetRequiredService<ITenantInvalidator<string>>();
        var invalidated = 0;
        var stop = false;

        // The readers watch the flag and the count the test changes as they run.
        async Task ReadAsync(bool byIdentifier)
        {
            // ReSharper disable once AccessToModifiedClosure
            while (!Volatile.Read(ref stop))
            {
                // ReSharper disable once AccessToModifiedClosure
                var minimum = Volatile.Read(ref invalidated);
                var tenant = byIdentifier
                    ? await tenants.FindByIdentifierAsync("acme")
                    : await tenants.GetTenantAsync("acme");
                int.Parse(tenant!.Name[1..], CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(minimum);
            }
        }

        var readers = Enumerable.Range(0, 8).Select(i => Task.Run(() => ReadAsync(i % 2 == 0))).ToList();

        for (var version = 2; version < 500; version++)
        {
            store.Version = version;
            await invalidator.InvalidateAsync("acme", TestContext.Current.CancellationToken);
            Volatile.Write(ref invalidated, version);
            await Task.Yield();
        }

        Volatile.Write(ref stop, true);
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WithoutCacheTenants_EveryLookupReadsTheStore_AndInvalidatingDoesNothing()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => _store));
        await using var provider = services.BuildServiceProvider();
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("acme", TestContext.Current.CancellationToken);
        await tenants.FindByIdentifierAsync("acme", TestContext.Current.CancellationToken);

        _store.Reads.Should().HaveCount(4);

        // Always registered, so code that invalidates runs whether or not tenants are cached.
        var cache = provider.GetRequiredService<ITenantInvalidator<string>>();
        await cache.InvalidateAsync("acme", TestContext.Current.CancellationToken);
        await cache.InvalidateAllAsync(TestContext.Current.CancellationToken);
        await cache.Awaiting(c => c.InvalidateAsync(null!, TestContext.Current.CancellationToken)).Should().ThrowAsync<ArgumentNullException>();
        await cache.Awaiting(c => c.InvalidateAsync("", TestContext.Current.CancellationToken)).Should().ThrowAsync<ArgumentException>().WithMessage("*reserved for \"no tenant\"*");
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
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();

        services.Should().ContainSingle(d => d.ServiceType == typeof(TenantStoreCacheOptions));
        services.Should().ContainSingle(d => d.ServiceType == typeof(TenantStoreCache<string>));
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
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();

        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromDays(3650));
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);

        _store.Reads.Should().ContainSingle();
    }

    [Fact]
    public void AFullCache_StopsGrowing_UntilItsEntriesExpire()
    {
        TenantStoreCache<string> cache = new(
            new TenantStoreCacheOptions { Duration = TimeSpan.FromMinutes(1) },
            _time);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalidating_RunsEveryHandler_WithOrWithoutCachedTenants(bool cacheTenants)
    {
        ServiceCollection services = new();
        Recorder first = new(), second = new();
        services.AddSingleton<ITenantInvalidationHandler<string>>(first);
        services.AddSingleton<ITenantInvalidationHandler<string>>(second);
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseStore(_ => _store);
            if (cacheTenants)
                tenant.CacheTenants();
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var cache = provider.GetRequiredService<ITenantInvalidator<string>>();

        await cache.InvalidateAsync("acme", TestContext.Current.CancellationToken);
        await cache.InvalidateAllAsync(TestContext.Current.CancellationToken);

        first.Calls.Should().Equal("acme", "*");
        second.Calls.Should().Equal("acme", "*");
    }

    [Fact]
    public async Task EveryHandlerRuns_WhenOneThrows_AndTheErrorsAreThrownAfterwards()
    {
        ServiceCollection services = new();
        Recorder last = new();
        services.AddSingleton<ITenantInvalidationHandler<string>>(new Throwing("first"));
        services.AddSingleton<ITenantInvalidationHandler<string>>(last);
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => _store).CacheTenants());
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ITenantInvalidator<string>>();
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);

        await cache.Awaiting(c => c.InvalidateAsync("acme", TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>().WithMessage("first");
        last.Calls.Should().Equal("acme");
        await tenants.GetTenantAsync("acme", TestContext.Current.CancellationToken);
        _store.Reads.Should().HaveCount(2, "the tenant was removed before the handlers ran");

        services.AddSingleton<ITenantInvalidationHandler<string>>(new Throwing("second"));
        await using var twoThrow = services.BuildServiceProvider();
        (await twoThrow.GetRequiredService<ITenantInvalidator<string>>().Awaiting(c => c.InvalidateAllAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<AggregateException>()).Which.InnerExceptions.Select(e => e.Message).Should().Equal("first", "second");
    }

    [Fact]
    public async Task InvalidatingAReservedId_IsRefused_BeforeAnyHandlerRuns()
    {
        ServiceCollection services = new();
        Recorder handler = new();
        services.AddSingleton<ITenantInvalidationHandler<int>>(handler);
        services.AddTenantry<int>(tenant => tenant.UseInMemoryStore([new TenantDescriptor<int> { TenantId = 7, Name = "Seven" }]).CacheTenants());
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ITenantInvalidator<int>>().Awaiting(c => c.InvalidateAsync(0, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("tenantId");
        handler.Calls.Should().BeEmpty("an empty or default id would read as every tenant to some handlers");
    }

    [Fact]
    public async Task AHandler_CanDependOnTheInvalidatorItself()
    {
        ServiceCollection services = new();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<string>, NeedsTheInvalidator>());
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => _store).CacheTenants());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await provider.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", TestContext.Current.CancellationToken);

        var handler = provider.GetServices<ITenantInvalidationHandler<string>>().OfType<NeedsTheInvalidator>().Single();
        handler.Calls.Should().Be(1);
        handler.Invalidator.Should().BeSameAs(provider.GetRequiredService<ITenantInvalidator<string>>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheInvalidator_RemovesTheCachedTenant_AndAwaitsEveryHandler(bool cacheTenants)
    {
        ServiceCollection services = new();
        Slow slow = new();
        Recorder recorder = new();
        services.AddSingleton<ITenantInvalidationHandler<string>>(slow);
        services.AddSingleton<ITenantInvalidationHandler<string>>(recorder);
        services.AddTenantry<string>(tenant =>
        {
            tenant.UseStore(_ => _store);
            if (cacheTenants)
                tenant.CacheTenants();
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var tenants = provider.GetRequiredService<ITenantLookup<string>>();
        var invalidator = provider.GetRequiredService<ITenantInvalidator<string>>();
        var ct = TestContext.Current.CancellationToken;
        await tenants.GetTenantAsync("acme", ct);

        await invalidator.InvalidateAsync("acme", ct);
        await tenants.GetTenantAsync("acme", ct);
        await invalidator.InvalidateAllAsync(ct);

        slow.Calls.Should().Equal("acme", "*");
        recorder.Calls.Should().Equal("acme", "*");
        _store.Reads.Should().HaveCount(2, "the cached tenant, if any, was removed");
        await FluentActions.Awaiting(() => invalidator.InvalidateAsync("", ct).AsTask()).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ACancelledInvalidation_StopsAtTheHandlerItReached()
    {
        ServiceCollection services = new();
        Recorder after = new();
        services.AddSingleton<ITenantInvalidationHandler<string>>(new Slow());
        services.AddSingleton<ITenantInvalidationHandler<string>>(after);
        services.AddTenantry<string>(tenant => tenant.UseStore(_ => _store));
        await using var provider = services.BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var token = cancelled.Token;

        await provider.GetRequiredService<ITenantInvalidator<string>>()
            .Awaiting(i => i.InvalidateAsync("acme", token).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
        after.Calls.Should().BeEmpty();
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

    private sealed class Recorder : ITenantInvalidationHandler<string>, ITenantInvalidationHandler<int>
    {
        public List<string> Calls { get; } = [];

        public ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken) => Record(tenantId);

        public ValueTask InvalidateAsync(int tenantId, CancellationToken cancellationToken) => Record($"{tenantId}");

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) => Record("*");

        private ValueTask Record(string call)
        {
            Calls.Add(call);
            return ValueTask.CompletedTask;
        }
    }

    // Completes later, as a remote cache's removal does.
    private sealed class Slow : ITenantInvalidationHandler<string>
    {
        public List<string> Calls { get; } = [];

        public async ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            Calls.Add(tenantId);
        }

        public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            Calls.Add("*");
        }
    }

    private sealed class Throwing(string message) : ITenantInvalidationHandler<string>
    {
        public ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken) => throw new InvalidOperationException(message);

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) => ValueTask.FromException(new InvalidOperationException(message));
    }

    private sealed class NeedsTheInvalidator(ITenantInvalidator<string> invalidator) : ITenantInvalidationHandler<string>
    {
        public ITenantInvalidator<string> Invalidator { get; } = invalidator;

        public int Calls { get; private set; }

        public ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // Matches identifiers without regard to case, as a database's default collation would.
    // Answers "acme" with the version current when the read begins; the next read can be held open.
    private sealed class VersionedStore : ITenantStore<string>
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _holdNext;
        private int _reads;

        public int Version { get; set; } = 1;

        public bool Yield { get; init; }

        public int Reads => Volatile.Read(ref _reads);

        public Task Held => _held.Task;

        public void HoldNextRead() => Volatile.Write(ref _holdNext, 1);

        public void Release() => _release.SetResult();

        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            ReadAsync();

        public ValueTask<ITenantDescriptor<string>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
            ReadAsync();

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private async ValueTask<ITenantDescriptor<string>?> ReadAsync()
        {
            Interlocked.Increment(ref _reads);
            TenantDescriptor<string> tenant = new() { TenantId = "acme", Name = $"v{Version}" };

            if (Interlocked.Exchange(ref _holdNext, 0) == 1)
            {
                _held.SetResult();
                await _release.Task;
            }
            else if (Yield)
            {
                await Task.Yield();
            }

            return tenant;
        }
    }

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
