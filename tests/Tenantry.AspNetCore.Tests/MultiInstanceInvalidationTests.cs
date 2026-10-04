using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tenantry.Caching;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// The multi-instance invalidation pattern of the tenant stores guide: each instance publishes its invalidations with
/// <c>BroadcastInvalidations</c>, and applies those it receives with <c>InvalidateLocallyAsync</c>. An in-process bus
/// stands in for Redis pub/sub or a message broker, and two hosts for two instances of the application.
/// </summary>
public sealed class MultiInstanceInvalidationTests
{
    private static readonly TenantDescriptor<string> Acme = new() { TenantId = "acme", Name = "Acme" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InvalidatingATenantOnOneInstance_ClearsItOnTheOther_ExactlyOnce()
    {
        Bus bus = new();
        CountingStore store = new();
        using var first = await StartAsync(bus, store);
        using var second = await StartAsync(bus, store);

        var before = await ReadAsync(second);
        (await ReadAsync(second)).Should().Be(before, "the second instance serves its cached copies");
        store.Reads.Should().Be(1);

        await first.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAsync("acme", Ct);

        var after = await ReadAsync(second);
        store.Reads.Should().Be(2, "the second instance's tenant cache was cleared");
        after.Options.Should().NotBe(before.Options, "its per-tenant options were cleared");
        after.Cached.Should().NotBe(before.Cached, "its HybridCache entries were removed");
        bus.Published.Should().Equal("acme");
        Invalidations(first).Should().Equal("acme");
        Invalidations(second).Should().Equal("acme");
    }

    [Fact]
    public async Task InvalidatingEveryTenant_ReachesTheOtherInstance_ExactlyOnce()
    {
        Bus bus = new();
        CountingStore store = new();
        using var first = await StartAsync(bus, store);
        using var second = await StartAsync(bus, store);
        await ReadAsync(second);

        await first.Services.GetRequiredService<ITenantInvalidator<string>>().InvalidateAllAsync(Ct);
        await ReadAsync(second);

        store.Reads.Should().Be(2);
        bus.Published.Should().Equal("*");
        Invalidations(first).Should().Equal("*");
        Invalidations(second).Should().Equal("*");
    }

    [Fact]
    public async Task APublishThatFails_IsThrownAfterThisInstanceIsInvalidated()
    {
        Bus bus = new() { Fail = true };
        CountingStore store = new();
        using var instance = await StartAsync(bus, store);
        var before = await ReadAsync(instance);

        await instance.Services.GetRequiredService<ITenantInvalidator<string>>()
            .Awaiting(invalidator => invalidator.InvalidateAsync("acme", Ct).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("The bus is down.");

        Invalidations(instance).Should().Equal("acme");
        (await ReadAsync(instance)).Cached.Should().NotBe(before.Cached);
        store.Reads.Should().Be(2);
    }

    private static async Task<IHost> StartAsync(Bus bus, CountingStore store)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(bus);
        builder.Services.AddSingleton<RecordingHandler>();
        builder.Services.AddHybridCache();
        builder.Services.AddTenantry<string>(tenant => tenant
            .UseStore(_ => store)
            .CacheTenants()
            .ConfigurePerTenant(perTenant => perTenant.Configure<PlanOptions>((options, _) => options.Built = Guid.NewGuid()))
            .IsolateCaches()
            .BroadcastInvalidations(sp => new BusPublisher(sp.GetRequiredService<Bus>(), sp.GetRequiredService<InstanceId>())));
        builder.Services.AddSingleton<ITenantInvalidationHandler<string>>(sp => sp.GetRequiredService<RecordingHandler>());
        builder.Services.AddSingleton<InstanceId>();
        builder.Services.AddHostedService<InvalidationSubscriber>();

        var host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }

    // What the second instance serves for acme: the tenant (through its cache), its options and a HybridCache entry.
    private static async Task<(Guid Options, Guid Cached)> ReadAsync(IHost instance)
    {
        var tenant = await instance.Services.GetRequiredService<ITenantLookup<string>>().GetTenantAsync("acme", Ct);
        using var current = instance.Services.GetRequiredService<ITenantContextSetter<string>>().MakeCurrent(tenant!);
        var options = instance.Services.GetRequiredService<IOptionsMonitor<PlanOptions>>().CurrentValue.Built;
        var cached = await instance.Services.GetRequiredService<HybridCache>()
            .GetOrCreateAsync("plan", _ => ValueTask.FromResult(Guid.NewGuid()), cancellationToken: Ct);
        return (options, cached);
    }

    private static List<string> Invalidations(IHost instance) =>
        [.. instance.Services.GetRequiredService<RecordingHandler>().Invalidations];

    public sealed class PlanOptions
    {
        public Guid Built { get; set; }
    }

    /// <summary>Stands in for the tenants' database, which every instance reads.</summary>
    private sealed class CountingStore : ITenantStore<string>
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            return ValueTask.FromResult<ITenantDescriptor<string>?>(tenantId == Acme.TenantId ? Acme : null);
        }

        public ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ITenantDescriptor<string>>>([Acme]);
    }

    /// <summary>An instance's identity, so it can ignore its own messages, as a Redis subscriber receives them too.</summary>
    private sealed class InstanceId
    {
        public Guid Value { get; } = Guid.NewGuid();
    }

    /// <summary>A message: a tenant's id, or <c>*</c> for every tenant, and the instance that published it.</summary>
    private sealed record Invalidation(string TenantId, Guid Sender);

    /// <summary>An in-process pub/sub channel that delivers each message to every subscriber, the publisher's own included.</summary>
    private sealed class Bus
    {
        private readonly ConcurrentBag<Func<Invalidation, Task>> _subscribers = [];
        private readonly ConcurrentQueue<string> _published = new();

        public bool Fail { get; init; }

        public IEnumerable<string> Published => _published;

        public void Subscribe(Func<Invalidation, Task> subscriber) => _subscribers.Add(subscriber);

        public async Task PublishAsync(Invalidation message)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The bus is down.");
            }

            _published.Enqueue(message.TenantId);
            await Task.WhenAll(_subscribers.Select(subscriber => subscriber(message)));
        }
    }

    /// <summary>The broadcasting handler: publishes what this instance invalidates.</summary>
    private sealed class BusPublisher(Bus bus, InstanceId instance) : ITenantInvalidationHandler<string>
    {
        public ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken) =>
            new(bus.PublishAsync(new Invalidation(tenantId, instance.Value)));

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) =>
            new(bus.PublishAsync(new Invalidation("*", instance.Value)));
    }

    /// <summary>Applies what other instances publish, locally, so it is not published again.</summary>
    private sealed class InvalidationSubscriber(Bus bus, InstanceId instance, ITenantInvalidator<string> invalidator)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            bus.Subscribe(async message =>
            {
                if (message.Sender == instance.Value)
                {
                    return;
                }

                if (message.TenantId == "*")
                {
                    await invalidator.InvalidateAllLocallyAsync();
                }
                else
                {
                    await invalidator.InvalidateLocallyAsync(message.TenantId);
                }
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Records the invalidations an instance applies.</summary>
    private sealed class RecordingHandler : ITenantInvalidationHandler<string>
    {
        public ConcurrentQueue<string> Invalidations { get; } = new();

        public ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken)
        {
            Invalidations.Enqueue(tenantId);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
        {
            Invalidations.Enqueue("*");
            return ValueTask.CompletedTask;
        }
    }
}
