using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tenantry.AspNetCore.Internal;

/// <summary>
/// Marks a request that <c>app.UseTenantry()</c> has handled, so code later in the pipeline can tell "no tenant" from
/// "not resolved yet".
/// </summary>
internal sealed class TenantResolutionFeature
{
    public static readonly TenantResolutionFeature Instance = new();
}

/// <summary>
/// The base policy <c>IsolateOutputCache()</c> adds: every cached response varies by the request's tenant, or by its
/// having none, and a tenant's responses are tagged with it, so invalidating the tenant evicts them. A response for a
/// request <c>app.UseTenantry()</c> did not handle (the output cache ran first, or the request went down a branch
/// without it) is not cached: its key, with no vary value, matches no stored response, and it is never stored.
/// </summary>
internal sealed class TenantOutputCachePolicy<TKey>(ITenantContext<TKey> tenantContext, ILogger logger) : IOutputCachePolicy
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    internal const string TenantKey = "tenantry-tenant";

    internal const string NoTenantKey = "tenantry-no-tenant";

    internal const string AllTenantsTag = "t:";

    private int _warned;

    internal static string TenantTag(string tenantId) => AllTenantsTag + tenantId;

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        // Whether app.UseTenantry() handled the request is decided here, as the cache sees it arrive: by the time the
        // response is stored, a later UseTenantry() has run and resolved the tenant the key does not name.
        if (context.HttpContext.Features.Get<TenantResolutionFeature>() is null)
        {
            context.HttpContext.Features.Set(UnresolvedRequest.Instance);
            return ValueTask.CompletedTask;
        }

        // A tenant's responses, and those for no tenant, vary by different keys, so no tenant id can name the latter.
        if (tenantContext.CurrentTenant is { } tenant)
        {
            var tenantId = TenantIds.Format(tenant.TenantId);
            context.CacheVaryByRules.VaryByValues[TenantKey] = tenantId;
            context.Tags.Add(TenantTag(tenantId));
            context.Tags.Add(AllTenantsTag);
        }
        else
        {
            context.CacheVaryByRules.VaryByValues[NoTenantKey] = "true";
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    // Endpoint policies run after the base policies, but only ever turn storage off here.
    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        if (context.HttpContext.Features.Get<UnresolvedRequest>() is not null && context.AllowCacheStorage)
        {
            context.AllowCacheStorage = false;

            if (context.EnableOutputCaching && Interlocked.Exchange(ref _warned, 1) == 0)
                TenantResolutionLog.OutputCacheBeforeTenantry(logger, context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        }

        return ValueTask.CompletedTask;
    }

    // A request the output cache saw before app.UseTenantry() handled it.
    private sealed class UnresolvedRequest
    {
        public static readonly UnresolvedRequest Instance = new();
    }
}

/// <summary>Adds <see cref="TenantOutputCachePolicy{TKey}"/> to the output cache's base policies, once.</summary>
internal sealed class TenantOutputCacheSetup<TKey>(ITenantContext<TKey> tenantContext, ILoggerFactory loggers)
    : IConfigureOptions<OutputCacheOptions>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Configure(OutputCacheOptions options) =>
        options.AddBasePolicy(new TenantOutputCachePolicy<TKey>(tenantContext, loggers.CreateLogger("Tenantry.AspNetCore.OutputCache")));
}

/// <summary>
/// Evicts a tenant's cached responses, by its tag, when <see cref="ITenantStoreCache{TKey}"/> invalidates the tenant,
/// and every tenant's when it invalidates them all. Waits for the eviction: invalidation is rare, and offboarding relies
/// on it being done. Without output caching (<c>AddOutputCache()</c>) there is nothing to evict.
/// </summary>
internal sealed class TenantOutputCacheInvalidation<TKey>(IServiceProvider services) : ITenantInvalidationHandler<TKey>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Invalidate(TKey tenantId) => Evict(TenantOutputCachePolicy<TKey>.TenantTag(TenantIds.Format(tenantId)));

    public void InvalidateAll() => Evict(TenantOutputCachePolicy<TKey>.AllTenantsTag);

    private void Evict(string tag)
    {
        if (services.GetService(typeof(IOutputCacheStore)) is not IOutputCacheStore store)
            return;

        var eviction = store.EvictByTagAsync(tag, CancellationToken.None);
        if (!eviction.IsCompletedSuccessfully)
            eviction.AsTask().GetAwaiter().GetResult();
    }
}
