using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tenantry.Caching.Internal;
using Tenantry.Tests.Shared;

namespace Tenantry.Caching.Tests;

/// <summary>
/// <c>IsolateCaches()</c>: the injected HybridCache keeps entries per tenant and refuses calls without one,
/// <see cref="SharedHybridCache"/> keeps entries every tenant shares, invalidating a tenant removes its entries, and
/// <see cref="ITenantDistributedCache"/> prefixes the registered distributed cache.
/// </summary>
public sealed class IsolateCachesTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };
    private static readonly TenantDescriptor<string> Globex = new() { TenantId = "globex", Name = "Globex" };

    private readonly InMemoryHybridCache _inner = new();

    [Fact]
    public async Task AnEntry_IsReadOnlyByTheTenantThatWroteIt()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();

        (await AsAsync(provider, Acme, () => cache.GetOrCreateAsync("orders", _ => ValueTask.FromResult("acme's"), cancellationToken: Ct)))
            .Should().Be("acme's");
        (await AsAsync(provider, Globex, () => cache.GetOrCreateAsync("orders", _ => ValueTask.FromResult("globex's"), cancellationToken: Ct)))
            .Should().Be("globex's");
        (await AsAsync(provider, Acme, () => cache.GetOrCreateAsync("orders", _ => ValueTask.FromResult("again"), cancellationToken: Ct)))
            .Should().Be("acme's");

        _inner.Entries.Keys.Should().BeEquivalentTo("t:acme:orders", "t:globex:orders");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFactory_RunsAsTheTenantThatCalled_AndASharedOneAsNoTenant_WhereverTheCacheRunsIt(bool cancellable)
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();
        var shared = provider.GetRequiredService<SharedHybridCache>();
        var tenantContext = provider.GetRequiredService<ITenantContext<string>>();
        var token = cancellable ? Ct : CancellationToken.None;

        var seen = await AsAsync(provider, Acme, () =>
            cache.GetOrCreateAsync("tenant", _ => ValueTask.FromResult(tenantContext.CurrentTenantId), cancellationToken: token));
        var sharedSeen = await AsAsync(provider, Acme, () =>
            shared.GetOrCreateAsync("tenant", _ => ValueTask.FromResult(tenantContext.CurrentTenantId ?? "none"), cancellationToken: token));

        seen.Should().Be("acme");
        sharedSeen.Should().Be("none");
        tenantContext.HasTenant.Should().BeFalse("the tenant is current only inside the factory and the caller's scope");
    }

    [Fact]
    public async Task TheTagStar_RemovesEveryEntryOfTheCurrentTenant_OrEverySharedEntry_AndNoOther()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();
        var shared = provider.GetRequiredService<SharedHybridCache>();
        await shared.SetAsync("rates", 1, cancellationToken: Ct);
        await shared.SetAsync("currencies", 2, tags: ["fx"], cancellationToken: Ct);
        foreach (var tenant in new[] { Acme, Globex })
            await AsAsync(provider, tenant, () => cache.SetAsync("orders", 1, cancellationToken: Ct).AsTask());

        await AsAsync(provider, Acme, () => cache.RemoveByTagAsync("*", Ct).AsTask());
        _inner.Entries.Keys.Should().BeEquivalentTo("s:rates", "s:currencies", "t:globex:orders");

        await shared.RemoveByTagAsync(["*"], Ct);
        _inner.Entries.Keys.Should().Equal("t:globex:orders");
    }

    [Fact]
    public async Task NullCollections_RemoveNothing_AsHybridCachesContractSays()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();
        var shared = provider.GetRequiredService<SharedHybridCache>();
        await shared.SetAsync("rates", 1, cancellationToken: Ct);

        await AsAsync(provider, Acme, async () =>
        {
            await cache.RemoveAsync((IEnumerable<string>)null!, Ct);
            await cache.RemoveByTagAsync((IEnumerable<string>)null!, Ct);
        });
        await shared.RemoveAsync((IEnumerable<string>)null!, Ct);
        await shared.RemoveByTagAsync((IEnumerable<string>)null!, Ct);

        _inner.Entries.Keys.Should().Equal("s:rates");
    }

    [Fact]
    public async Task EachEntry_IsTaggedWithItsTenant_AndItsOwnTagsArePrefixed()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();

        await AsAsync(provider, Acme, () => cache.SetAsync("orders", 1, tags: ["recent"], cancellationToken: Ct).AsTask());

        _inner.Entries["t:acme:orders"].Tags.Should().Equal("t:acme", "t:", "t:acme:recent");
    }

    [Fact]
    public async Task IdsWithTheSeparator_CannotNameAnotherTenantsEntry()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();

        await AsAsync(provider, new TenantDescriptor<string> { TenantId = "a:b", Name = "AB" }, () => cache.SetAsync("c", "a:b's", cancellationToken: Ct).AsTask());
        await AsAsync(provider, new TenantDescriptor<string> { TenantId = "a", Name = "A" }, () => cache.SetAsync("b:c", "a's", cancellationToken: Ct).AsTask());
        await AsAsync(provider, new TenantDescriptor<string> { TenantId = @"a\", Name = "A slash" }, () => cache.SetAsync("x", "slash", cancellationToken: Ct).AsTask());

        _inner.Entries.Keys.Should().BeEquivalentTo(@"t:a\:b:c", "t:a:b:c", @"t:a\\:x");
    }

    [Fact]
    public async Task WithoutATenant_EveryCallThrows_RatherThanUseAnEntryNoTenantOwns()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();

        await FluentActions.Awaiting(() => cache.GetOrCreateAsync("orders", _ => ValueTask.FromResult(1), cancellationToken: Ct).AsTask())
            .Should().ThrowAsync<TenantNotResolvedException>().WithMessage("*SharedHybridCache*");
        await FluentActions.Awaiting(() => cache.SetAsync("orders", 1, cancellationToken: Ct).AsTask()).Should().ThrowAsync<TenantNotResolvedException>();
        await FluentActions.Awaiting(() => cache.RemoveAsync("orders", Ct).AsTask()).Should().ThrowAsync<TenantNotResolvedException>();
        await FluentActions.Awaiting(() => cache.RemoveAsync(["orders"], Ct).AsTask()).Should().ThrowAsync<TenantNotResolvedException>();
        await FluentActions.Awaiting(() => cache.RemoveByTagAsync("recent", Ct).AsTask()).Should().ThrowAsync<TenantNotResolvedException>();
        await FluentActions.Awaiting(() => cache.RemoveByTagAsync(["recent"], Ct).AsTask()).Should().ThrowAsync<TenantNotResolvedException>();

        _inner.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovingByKeyOrTag_ReachesOnlyTheCurrentTenantsEntries()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();
        foreach (var tenant in new[] { Acme, Globex })
        {
            await AsAsync(provider, tenant, async () =>
            {
                await cache.SetAsync("a", 1, tags: ["recent"], cancellationToken: Ct);
                await cache.SetAsync("b", 2, cancellationToken: Ct);
                await cache.SetAsync("c", 3, cancellationToken: Ct);
            });
        }

        await AsAsync(provider, Acme, async () =>
        {
            await cache.RemoveByTagAsync(["recent"], Ct);
            await cache.RemoveAsync("b", Ct);
            await cache.RemoveAsync(["c"], Ct);
        });

        _inner.Entries.Keys.Should().BeEquivalentTo("t:globex:a", "t:globex:b", "t:globex:c");
    }

    [Fact]
    public async Task TheSharedCache_WorksWithoutATenant_AndItsEntriesNeverShareANameWithATenants()
    {
        await using var provider = Build();
        var shared = provider.GetRequiredService<SharedHybridCache>();
        var cache = provider.GetRequiredService<HybridCache>();

        await shared.SetAsync("t:acme:orders", "shared", tags: ["rates"], cancellationToken: Ct);
        await AsAsync(provider, Acme, () => cache.SetAsync("orders", "acme's", cancellationToken: Ct).AsTask());

        (await shared.GetOrCreateAsync("t:acme:orders", _ => ValueTask.FromResult("missed"), cancellationToken: Ct)).Should().Be("shared");
        _inner.Entries["s:t:acme:orders"].Tags.Should().Equal("s:", "s:rates");

        await shared.RemoveByTagAsync(["rates"], Ct);
        await shared.SetAsync("x", 1, cancellationToken: Ct);
        await shared.RemoveAsync(["x"], Ct);
        await shared.SetAsync("y", 1, cancellationToken: Ct);
        await shared.RemoveAsync("y", Ct);
        await shared.SetAsync("z", 1, tags: ["z"], cancellationToken: Ct);
        await shared.RemoveByTagAsync("z", Ct);
        _inner.Entries.Keys.Should().Equal("t:acme:orders");
    }

    [Fact]
    public async Task InvalidatingATenant_RemovesItsEntries_AndInvalidatingAll_RemovesEveryTenantsButNoSharedEntry()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<HybridCache>();
        var tenants = provider.GetRequiredService<ITenantInvalidator<string>>();
        await provider.GetRequiredService<SharedHybridCache>().SetAsync("rates", 1, cancellationToken: Ct);
        foreach (var tenant in new[] { Acme, Globex, new TenantDescriptor<string> { TenantId = "initech", Name = "Initech" } })
            await AsAsync(provider, tenant, () => cache.SetAsync("orders", 1, cancellationToken: Ct).AsTask());

        await tenants.InvalidateAsync("acme", TestContext.Current.CancellationToken);
        _inner.Entries.Keys.Should().BeEquivalentTo("s:rates", "t:globex:orders", "t:initech:orders");

        await tenants.InvalidateAllAsync(TestContext.Current.CancellationToken);
        _inner.Entries.Keys.Should().Equal("s:rates");
    }

    [Fact]
    public async Task WithoutAHybridCacheRegisteredFirst_TheCacheThrowsWhenUsed_NamingTheFix()
    {
        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        services.TryAddSingleton<HybridCache>(_inner);   // as AddHybridCache() adds it, only if none is registered
        await using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);
        var cache = provider.GetRequiredService<HybridCache>();

        await FluentActions.Awaiting(() => AsAsync(provider, Acme, () => cache.SetAsync("orders", 1, cancellationToken: Ct).AsTask()))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddHybridCache(), then builder.Services.AddTenantry*");
        await FluentActions.Awaiting(() => cache.GetOrCreateAsync("orders", _ => ValueTask.FromResult(1), cancellationToken: Ct).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => cache.RemoveAsync("orders", Ct).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => cache.RemoveByTagAsync("x", Ct).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        await provider.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AHybridCacheRegisteredAfterAddTenantry_StopsTheHost_RatherThanShareEntries()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<HybridCache>(_inner);
        builder.Services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        builder.Services.AddSingleton<HybridCache, InMemoryHybridCache>();   // replaces the isolated cache
        using var host = builder.Build();

        await FluentActions.Awaiting(() => host.StartAsync(Ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*InMemoryHybridCache, registered after AddTenantry*");
    }

    [Fact]
    public async Task AKeyedHybridCache_IsKeyedByTenantToo_WithItsOwnSharedEntries_AndInvalidatingATenantClearsIt()
    {
        InMemoryHybridCache reports = new();
        ServiceCollection services = new();
        services.AddSingleton<HybridCache>(_inner);
        services.AddKeyedSingleton<HybridCache>("reports", reports);
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme, Globex]).IsolateCaches());
        await using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);
        var cache = provider.GetRequiredKeyedService<HybridCache>("reports");

        RunStartupChecks(provider);
        await AsAsync(provider, Acme, () => cache.SetAsync("monthly", 1, cancellationToken: Ct).AsTask());
        await provider.GetRequiredKeyedService<SharedHybridCache>("reports").SetAsync("template", 2, cancellationToken: Ct);
        (await AsAsync(provider, Globex, () => cache.GetOrCreateAsync("monthly", _ => ValueTask.FromResult(0), cancellationToken: Ct)))
            .Should().Be(0, "Acme's entry is not Globex's");

        reports.Entries.Keys.Should().BeEquivalentTo("t:acme:monthly", "t:globex:monthly", "s:template");
        _inner.Entries.Should().BeEmpty("the keyed cache keeps its own entries");

        await provider.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", Ct);
        reports.Entries.Keys.Should().BeEquivalentTo("t:globex:monthly", "s:template");
    }

    [Fact]
    public void AKeyedHybridCacheRegisteredAfterAddTenantry_OrForAnyKey_StopsTheHost()
    {
        ServiceCollection after = new();
        after.AddSingleton<HybridCache>(_inner);
        after.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        after.AddKeyedSingleton<HybridCache>("reports", new InMemoryHybridCache());
        using (var provider = after.BuildServiceProvider(Conformance.ProviderOptions))
        {
            provider.Invoking(RunStartupChecks)
                .Should().Throw<InvalidOperationException>().WithMessage("*key 'reports' was registered after AddTenantry*");
        }

        ServiceCollection anyKey = new();
        anyKey.AddSingleton<HybridCache>(_inner);
        anyKey.AddKeyedSingleton<HybridCache>(KeyedService.AnyKey, (_, _) => new InMemoryHybridCache());
        anyKey.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        using (var provider = anyKey.BuildServiceProvider(Conformance.ProviderOptions))
        {
            provider.Invoking(RunStartupChecks)
                .Should().Throw<InvalidOperationException>().WithMessage("*any key*");
        }
    }

    // The check a host runs before it starts (ValidateOnStart), without building one: reading the options validates them.
    private static void RunStartupChecks(IServiceProvider provider) =>
        _ = provider.GetRequiredService<IOptions<CacheIsolationCheck>>().Value;

    [Fact]
    public async Task TheHostStarts_WithTheCacheRegisteredFirst_OrWithNoHybridCacheAtAll()
    {
        await using (var provider = Build())
            RunStartupChecks(provider);

        ServiceCollection services = new();
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        await using (var provider = services.BuildServiceProvider(Conformance.ProviderOptions))
            RunStartupChecks(provider);
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("factory")]
    [InlineData("type")]
    public async Task EveryKindOfRegistration_IsWrapped_AndACacheCreatedForIt_IsDisposedWithIt(string kind)
    {
        ServiceCollection services = new();
        switch (kind)
        {
            case "instance":
                services.AddSingleton<HybridCache>(_inner);
                break;
            case "factory":
                services.AddSingleton<HybridCache>(_ => _inner);
                break;
            default:
                services.AddSingleton<HybridCache, InMemoryHybridCache>();
                break;
        }

        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches().IsolateCaches());
        InMemoryHybridCache inner;
        await using (var provider = services.BuildServiceProvider(Conformance.ProviderOptions))
        {
            var cache = provider.GetRequiredService<HybridCache>();
            await AsAsync(provider, Acme, () => cache.SetAsync("orders", 1, cancellationToken: Ct).AsTask());
            inner = (InMemoryHybridCache)provider.GetRequiredService<SharedHybridCache>().Inner;
            inner.Entries.Keys.Should().Equal("t:acme:orders");
        }

        services.Count(d => d.ServiceType == typeof(HybridCache)).Should().Be(1);
        inner.Disposed.Should().Be(kind != "instance", "the container disposes what it created, not an instance it was given");
    }

    [Fact]
    public async Task TheTenantDistributedCache_PrefixesTheRegisteredOne_WhichStaysAsItIs()
    {
        ServiceCollection services = new();
        RecordingDistributedCache distributed = new();
        services.AddSingleton<IDistributedCache>(distributed);
        services.AddSingleton<HybridCache>(_inner);
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        await using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);
        var cache = provider.GetRequiredService<ITenantDistributedCache>();
        DistributedCacheEntryOptions options = new();

        provider.GetRequiredService<IDistributedCache>().Should().BeSameAs(distributed);
        await AsAsync(provider, Acme, async () =>
        {
            cache.Set("a", [1], options);
            await cache.SetAsync("b", [2], options, Ct);
            cache.Get("a").Should().Equal(1);
            (await cache.GetAsync("b", Ct)).Should().Equal(2);
            cache.Refresh("a");
            await cache.RefreshAsync("b", Ct);
            cache.Remove("a");
            await cache.RemoveAsync("b", Ct);
        });

        distributed.Calls.Should().Equal(
            "Set t:acme:a", "Set t:acme:b", "Get t:acme:a", "Get t:acme:b", "Refresh t:acme:a", "Refresh t:acme:b",
            "Remove t:acme:a", "Remove t:acme:b");
        cache.Invoking(c => c.Get("a")).Should().Throw<TenantNotResolvedException>();
        distributed.Calls.Should().HaveCount(8);
    }

    [Fact]
    public async Task EveryRegistration_Resolves_InAValidatedHost()
    {
        ServiceCollection services = new();
        services.AddSingleton<IDistributedCache>(new RecordingDistributedCache());
        services.AddSingleton<HybridCache>(_inner);
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme]).IsolateCaches());
        await using var provider = services.BuildServiceProvider(Conformance.ProviderOptions);
        await using var scope = provider.CreateAsyncScope();

        Conformance.ResolveEveryTenantryService(services, scope.ServiceProvider);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Build()
    {
        ServiceCollection services = new();
        services.AddSingleton<HybridCache>(_inner);
        services.AddTenantry<string>(tenant => tenant.UseInMemoryStore([Acme, Globex]).IsolateCaches());
        return services.BuildServiceProvider(Conformance.ProviderOptions);
    }

    private static async Task<T> AsAsync<T>(IServiceProvider provider, ITenantDescriptor<string> tenant, Func<ValueTask<T>> work)
    {
        using var _ = provider.GetRequiredService<ITenantContextSetter<string>>().Use(tenant);
        return await work();
    }

    private static async Task AsAsync(IServiceProvider provider, ITenantDescriptor<string> tenant, Func<Task> work)
    {
        using var _ = provider.GetRequiredService<ITenantContextSetter<string>>().Use(tenant);
        await work();
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new();

        public List<string> Calls { get; } = [];

        public byte[]? Get(string key)
        {
            Calls.Add($"Get {key}");
            return _entries.GetValueOrDefault(key);
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Calls.Add($"Set {key}");
            _entries[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) => Calls.Add($"Refresh {key}");

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            Refresh(key);
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            Calls.Add($"Remove {key}");
            _entries.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
