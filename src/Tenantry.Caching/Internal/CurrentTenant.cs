namespace Tenantry.Caching.Internal;

/// <summary>
/// The current tenant, for the caches, which have no key type: <c>IsolateCaches()</c> registers it for the
/// application's.
/// </summary>
internal interface ICurrentTenant
{
    /// <summary>The current tenant, captured to name its entries and to run a cache's factory as it, or null.</summary>
    ICapturedTenant? Capture();
}

/// <summary>A tenant captured where a cache was called.</summary>
internal interface ICapturedTenant
{
    /// <summary>The prefix of the tenant's keys and tags (<see cref="TenantCacheKeys.TenantPrefix"/>).</summary>
    string Prefix { get; }

    /// <summary>Makes the tenant current again, until the result is disposed.</summary>
    IDisposable MakeCurrent();
}

internal sealed class CurrentTenant<TKey>(ITenantContextSetter<TKey> tenantContext) : ICurrentTenant
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    public ICapturedTenant? Capture() =>
        tenantContext.CurrentTenant is { } tenant ? new Captured(tenant, tenantContext) : null;

    private sealed class Captured(ITenantDescriptor<TKey> tenant, ITenantContextSetter<TKey> tenantContext) : ICapturedTenant
    {
        public string Prefix { get; } = TenantCacheKeys.TenantPrefix(TenantIds.Format(tenant.TenantId));

        public IDisposable MakeCurrent() => tenantContext.MakeCurrent(tenant);
    }
}

internal static class CurrentTenantExtensions
{
    // A cache call that needs a tenant and has none fails, rather than reading or writing an entry no tenant owns.
    public static ICapturedTenant Require(this ICurrentTenant currentTenant, string cacheName) =>
        currentTenant.Capture() ?? throw new TenantNotResolvedException(
            $"No tenant is current, and {cacheName} keeps entries per tenant (IsolateCaches()). Use it while a tenant is " +
            "current, or inject SharedHybridCache for entries every tenant shares.");
}
