using System.Net;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Tenantry;
using Tenantry.Caching;

// Each check prints what it saw; the process fails on the first that differs.
TenantDescriptor<string> acme = new() { TenantId = "acme", Name = "Acme" };

ServiceCollection services = new();
services.AddSingleton<HybridCache, DictionaryCache>();
services.AddTenantry<string>(tenant => tenant
    .UseInMemoryStore([acme])
    .AddHttpPropagation()
    .IsolateCaches());
services.AddHttpClient("service", client => client.BaseAddress = new Uri("http://service.internal"))
    .UseTenantry()
    .ConfigurePrimaryHttpMessageHandler(() => new EchoTenant());

await using var provider = services.BuildServiceProvider();
var cache = provider.GetRequiredService<HybridCache>();
var tenantContext = provider.GetRequiredService<ITenantContextSetter<string>>();

using (tenantContext.Use(acme))
{
    Expect("HybridCache", await cache.GetOrCreateAsync("plan", _ => ValueTask.FromResult(tenantContext.CurrentTenantId)), "acme");
    Expect("HybridCache with state", await cache.GetOrCreateAsync("double", 21, (n, _) => ValueTask.FromResult(n * 2)), 42);
    Expect("HTTP propagation", await provider.GetRequiredService<IHttpClientFactory>().CreateClient("service").GetStringAsync("/"), "acme");
}

Expect("SharedHybridCache", await provider.GetRequiredService<SharedHybridCache>().GetOrCreateAsync("rates", _ => ValueTask.FromResult("shared")), "shared");
provider.GetRequiredService<ITenantStoreCache<string>>().Invalidate("acme");
Expect("invalidating the tenant leaves the shared entry", string.Join(",", DictionaryCache.Last!.Keys), "s:rates");
Console.WriteLine("Native AOT smoke test passed.");
return 0;

static void Expect<T>(string check, T actual, T expected)
{
    Console.WriteLine($"{check}: {actual}");
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine($"{check}: expected {expected}");
        Environment.Exit(1);
    }
}

// A small HybridCache: entries by key, removed by tag.
internal sealed class DictionaryCache : HybridCache
{
    public static DictionaryCache? Last { get; private set; }

    private readonly Dictionary<string, (object? Value, string[] Tags)> _entries = new();

    public DictionaryCache() => Last = this;

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public override async ValueTask<T> GetOrCreateAsync<TState, T>(
        string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(key, out var entry))
            return (T)entry.Value!;

        var value = await factory(state, cancellationToken);
        _entries[key] = (value, tags?.ToArray() ?? []);
        return value;
    }

    public override ValueTask SetAsync<T>(
        string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        _entries[key] = (value, tags?.ToArray() ?? []);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.Remove(key);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        foreach (var key in _entries.Where(entry => entry.Value.Tags.Contains(tag)).Select(entry => entry.Key).ToList())
            _entries.Remove(key);

        return ValueTask.CompletedTask;
    }
}

// The called service: answers with the tenant the request carried.
internal sealed class EchoTenant : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.Headers.TryGetValues(TenantPropagation.HeaderName, out var values) ? values.Single() : ""),
        });
}
