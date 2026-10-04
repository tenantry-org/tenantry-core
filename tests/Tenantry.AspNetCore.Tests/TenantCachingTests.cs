using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tenantry.Caching;
using Tenantry.Tests.Shared;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// Caches kept per tenant in a web application, with ASP.NET Core's own implementations: the output cache
/// (<c>IsolateOutputCache()</c>) and Microsoft.Extensions.Caching.Hybrid's HybridCache over a distributed cache
/// (<c>IsolateCaches()</c>).
/// </summary>
public sealed class TenantCachingTests
{
    [Fact]
    public async Task ACachedResponse_IsServedOnlyToTheTenantItWasCachedFor()
    {
        await using var app = await StartAsync();
        using var acme = Client(app, "acme");
        using var globex = Client(app, "globex");

        var first = await acme.GetStringAsync("/now", Ct);
        (await acme.GetStringAsync("/now", Ct)).Should().Be(first, "acme's response is cached");
        (await globex.GetStringAsync("/now", Ct)).Should().NotBe(first, "globex gets its own");
    }

    [Fact]
    public async Task AResponseWithoutATenant_IsCachedApartFromEveryTenants()
    {
        await using var app = await StartAsync();
        using var anonymous = app.GetTestClient();
        using var acme = Client(app, "acme");

        var withoutTenant = await anonymous.GetStringAsync("/public", Ct);
        (await anonymous.GetStringAsync("/public", Ct)).Should().Be(withoutTenant);
        (await acme.GetStringAsync("/public", Ct)).Should().NotBe(withoutTenant);
    }

    [Fact]
    public async Task InvalidatingATenant_EvictsItsCachedResponses_AndNoOthers()
    {
        await using var app = await StartAsync();
        using var acme = Client(app, "acme");
        using var globex = Client(app, "globex");
        var acmeFirst = await acme.GetStringAsync("/now", Ct);
        var globexFirst = await globex.GetStringAsync("/now", Ct);

        await app.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", TestContext.Current.CancellationToken);

        (await acme.GetStringAsync("/now", Ct)).Should().NotBe(acmeFirst);
        (await globex.GetStringAsync("/now", Ct)).Should().Be(globexFirst);

        await app.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAllAsync(TestContext.Current.CancellationToken);
        (await globex.GetStringAsync("/now", Ct)).Should().NotBe(globexFirst);
    }

    [Fact]
    public async Task TheOutputCacheBeforeUseTenantry_CachesNothing_AndSaysSoOnce()
    {
        RecordingLoggerProvider logs = new();
        await using var app = await StartAsync(outputCacheFirst: true, logs: logs);
        using var acme = Client(app, "acme");
        using var globex = Client(app, "globex");

        var first = await acme.GetStringAsync("/now", Ct);
        (await acme.GetStringAsync("/now", Ct)).Should().NotBe(first);
        (await globex.GetStringAsync("/now", Ct)).Should().NotBe(first, "nothing acme got is served to globex");

        logs.Messages.Where(message => message.Contains("output cache ran before app.UseTenantry()", StringComparison.Ordinal))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task ARequestOnABranchWithoutUseTenantry_IsServed_AndNotCached()
    {
        await using var app = await StartAsync(tenantryOnlyOutside: "/health");
        using var client = app.GetTestClient();

        var first = await client.GetStringAsync("/health", Ct);
        (await client.GetStringAsync("/health", Ct)).Should().NotBe(first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HybridCacheFactories_RunAsTheCallingTenant_AndSharedOnesAsNoTenant_WhateverTheToken(bool cancellable)
    {
        await using var app = await StartAsync();
        var cache = app.Services.GetRequiredService<HybridCache>();
        var shared = app.Services.GetRequiredService<SharedHybridCache>();
        var tenants = app.Services.GetRequiredService<ITenantContextSetter<string>>();
        using CancellationTokenSource source = new();
        var token = cancellable ? source.Token : CancellationToken.None;

        using (tenants.MakeCurrent(new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }))
        {
            (await cache.GetOrCreateAsync($"seen-{cancellable}", _ => ValueTask.FromResult(tenants.CurrentTenantId), cancellationToken: token))
                .Should().Be("acme");
            (await shared.GetOrCreateAsync($"seen-{cancellable}", _ => ValueTask.FromResult(tenants.CurrentTenantId ?? "no tenant"), cancellationToken: token))
                .Should().Be("no tenant", "a shared entry is never loaded as one tenant");
        }
    }

    [Fact]
    public async Task HybridCacheEntries_ArePerTenant_InBothLevels_AndInvalidatingATenantRemovesItsEntries()
    {
        await using var app = await StartAsync();
        using var acme = Client(app, "acme");
        using var globex = Client(app, "globex");

        (await acme.GetStringAsync("/plan", Ct)).Should().Be("acme 1");
        (await globex.GetStringAsync("/plan", Ct)).Should().Be("globex 2");
        (await acme.GetStringAsync("/plan", Ct)).Should().Be("acme 1", "acme's entry is cached");
        (await app.GetTestClient().GetStringAsync("/rate", Ct)).Should().Be("rate 3", "shared entries work without a tenant");

        // The second level holds the entries under each tenant's prefix, and the shared one under its own. HybridCache
        // can write them after it has returned the value, so wait for each write.
        var distributed = (DictionaryDistributedCache)app.Services.GetRequiredService<IDistributedCache>();
        await Task.WhenAll(distributed.Written("t:acme:plan"), distributed.Written("t:globex:plan"), distributed.Written("s:rate"))
            .WaitAsync(TimeSpan.FromSeconds(30), Ct);
        (await distributed.GetAsync("t:acme:plan", Ct)).Should().NotBeNull();
        (await distributed.GetAsync("t:globex:plan", Ct)).Should().NotBeNull();
        (await distributed.GetAsync("s:rate", Ct)).Should().NotBeNull();

        await app.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", TestContext.Current.CancellationToken);

        (await acme.GetStringAsync("/plan", Ct)).Should().Be("acme 4");
        (await globex.GetStringAsync("/plan", Ct)).Should().Be("globex 2");
        (await app.GetTestClient().GetStringAsync("/rate", Ct)).Should().Be("rate 3");
    }

    [Fact]
    public async Task AddHybridCacheAfterAddTenantry_StopsTheHost_NamingTheOrder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTenantry<string>(tenant => tenant
            .UseInMemoryStore([new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" }])
            .IsolateCaches());
        builder.Services.AddHybridCache();
        await using var app = builder.Build();

        await FluentActions.Awaiting(() => app.StartAsync(Ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("AddHybridCache() was called after AddTenantry*");
    }

    [Fact]
    public async Task EveryCachingService_Resolves_InAValidatedHost()
    {
        await using var app = await StartAsync();
        await using var scope = app.Services.CreateAsyncScope();

        Conformance.ResolveEveryTenantryService(Services(app), scope.ServiceProvider);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient Client(WebApplication app, string tenant)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        return client;
    }

    private static IServiceCollection Services(WebApplication app) => app.Services.GetRequiredService<ServicesHolder>().Services;

    private static async Task<WebApplication> StartAsync(
        bool outputCacheFirst = false, RecordingLoggerProvider? logs = null, string? tenantryOnlyOutside = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = Conformance.ProviderOptions.ValidateScopes;
            options.ValidateOnBuild = Conformance.ProviderOptions.ValidateOnBuild;
        });
        builder.WebHost.UseTestServer();
        if (logs is not null)
            builder.Logging.AddProvider(logs);

        builder.Services.AddOutputCache();
        // Not AddDistributedMemoryCache(): HybridCache does not use MemoryDistributedCache as its second level.
        builder.Services.AddSingleton<IDistributedCache, DictionaryDistributedCache>();
        builder.Services.AddHybridCache();
        builder.Services.AddTenantry<string>(tenant => tenant
            .ResolveFromHeader("X-Tenant-Id")
            .UseInMemoryStore(
            [
                new TenantDescriptor<string> { TenantId = "acme", Name = "Acme" },
                new TenantDescriptor<string> { TenantId = "globex", Name = "Globex" },
            ])
            .IsolateOutputCache()
            .IsolateCaches());
        builder.Services.AddSingleton(new ServicesHolder(builder.Services));

        var app = builder.Build();
        if (outputCacheFirst)
        {
            app.UseOutputCache();
            app.UseTenantry();
        }
        else if (tenantryOnlyOutside is { } path)
        {
            app.UseWhen(context => !context.Request.Path.StartsWithSegments(path), branch => branch.UseTenantry());
            app.UseOutputCache();
        }
        else
        {
            app.UseTenantry();
            app.UseOutputCache();
        }

        var counter = 0;
        app.MapGet("/now", () => Guid.NewGuid().ToString()).CacheOutput().RequireTenant();
        app.MapGet("/public", () => Guid.NewGuid().ToString()).CacheOutput().AllowMissingTenant();
        app.MapGet("/health", () => Guid.NewGuid().ToString()).CacheOutput().AllowMissingTenant();
        app.MapGet("/plan", async (HybridCache cache, ITenantContext<string> tenant, CancellationToken ct) =>
            await cache.GetOrCreateAsync("plan", _ => ValueTask.FromResult($"{tenant.CurrentTenantId} {Interlocked.Increment(ref counter)}"), cancellationToken: ct))
            .RequireTenant();
        app.MapGet("/rate", async (SharedHybridCache cache, CancellationToken ct) =>
            await cache.GetOrCreateAsync("rate", _ => ValueTask.FromResult($"rate {Interlocked.Increment(ref counter)}"), cancellationToken: ct))
            .AllowMissingTenant();

        await app.StartAsync(Ct);
        return app;
    }

    private sealed record ServicesHolder(IServiceCollection Services);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(RecordingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Messages.Enqueue(formatter(state, exception));
        }
    }

    private sealed class DictionaryDistributedCache : IDistributedCache
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _entries = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource> _written = new();

        public byte[]? Get(string key) => _entries.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _entries[key] = value;
            Writes(key).TrySetResult();
        }

        /// <summary>Completes once <paramref name="key"/> has been written, at any time before or after the call.</summary>
        public Task Written(string key) => Writes(key).Task;

        private TaskCompletionSource Writes(string key) =>
            _written.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _entries.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
