using System.Runtime.CompilerServices;

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

    /// <summary>
    /// The tenant's tag, which every entry of the tenant has (<see cref="TenantCacheKeys.TenantTag"/>).
    /// </summary>
    string Tag { get; }

    /// <summary>
    /// The tags of an entry written with no tags of the caller's: <see cref="Tag"/> and
    /// <see cref="TenantCacheKeys.AllTenantsTag"/>.
    /// </summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>Makes the tenant current again, until the result is disposed.</summary>
    IDisposable MakeCurrent();
}

internal sealed class CurrentTenant<TKey>(ITenantContextSetter<TKey> tenantContext) : ICurrentTenant
    where TKey : IEquatable<TKey>, IParsable<TKey>
{
    // Every cache call names its keys from the tenant, so each descriptor's prefix and tags are built once, not on
    // every call. A descriptor lives as long as the request's, or CacheTenants', reference to it.
    private readonly ConditionalWeakTable<ITenantDescriptor<TKey>, Captured> _captured = new();

    public ICapturedTenant? Capture()
    {
        if (tenantContext.CurrentTenant is not { } tenant)
            return null;

        // A descriptor whose id changed after it was captured is captured again, so it never writes under the old id.
        if (_captured.TryGetValue(tenant, out var captured)
            && EqualityComparer<TKey>.Default.Equals(captured.TenantId, tenant.TenantId))
        {
            return captured;
        }

        captured = new Captured(tenant, tenantContext);
        _captured.AddOrUpdate(tenant, captured);
        return captured;
    }

    private sealed class Captured : ICapturedTenant
    {
        private readonly ITenantDescriptor<TKey> _tenant;
        private readonly ITenantContextSetter<TKey> _tenantContext;

        public Captured(ITenantDescriptor<TKey> tenant, ITenantContextSetter<TKey> tenantContext)
        {
            _tenant = tenant;
            _tenantContext = tenantContext;
            TenantId = tenant.TenantId;

            var tenantId = TenantIds.Format(TenantId);
            Prefix = TenantCacheKeys.TenantPrefix(tenantId);
            Tag = TenantCacheKeys.TenantTag(tenantId);
            Tags = Array.AsReadOnly([Tag, TenantCacheKeys.AllTenantsTag]);
        }

        /// <summary>The id the prefix was built from.</summary>
        public TKey TenantId { get; }

        public string Prefix { get; }

        public string Tag { get; }

        public IReadOnlyList<string> Tags { get; }

        public IDisposable MakeCurrent() => _tenantContext.MakeCurrent(_tenant);
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
