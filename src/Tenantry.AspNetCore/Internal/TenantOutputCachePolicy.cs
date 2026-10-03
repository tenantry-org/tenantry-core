using Microsoft.AspNetCore.OutputCaching;
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
/// having none, and a tenant's responses are tagged with it, so invalidating the tenant evicts them. A request the output
/// cache sees before <c>app.UseTenantry()</c> has resolved it throws, rather than being cached for every tenant.
/// </summary>
internal sealed class TenantOutputCachePolicy<TKey>(ITenantContext<TKey> tenantContext) : IOutputCachePolicy
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    internal const string VaryKey = "tenantry-tenant";

    internal const string AllTenantsTag = "t:";

    internal static string TenantTag(string tenantId) => AllTenantsTag + tenantId;

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        if (context.HttpContext.Features.Get<TenantResolutionFeature>() is null)
        {
            throw new InvalidOperationException(
                "The output cache ran before app.UseTenantry(), so it cannot keep responses per tenant " +
                "(IsolateOutputCache()). Call app.UseTenantry() before app.UseOutputCache().");
        }

        if (tenantContext.CurrentTenant is { } tenant)
        {
            var tenantId = TenantIds.Format(tenant.TenantId);
            context.CacheVaryByRules.VaryByValues[VaryKey] = tenantId;
            context.Tags.Add(TenantTag(tenantId));
            context.Tags.Add(AllTenantsTag);
        }
        else
        {
            // An endpoint that allows a missing tenant: its responses without one are cached apart from every tenant's.
            context.CacheVaryByRules.VaryByValues[VaryKey] = string.Empty;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;
}

/// <summary>Adds <see cref="TenantOutputCachePolicy{TKey}"/> to the output cache's base policies, once.</summary>
internal sealed class TenantOutputCacheSetup<TKey>(ITenantContext<TKey> tenantContext) : IConfigureOptions<OutputCacheOptions>
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public void Configure(OutputCacheOptions options) => options.AddBasePolicy(new TenantOutputCachePolicy<TKey>(tenantContext));
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
