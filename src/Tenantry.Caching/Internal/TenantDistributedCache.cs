using Microsoft.Extensions.Caching.Distributed;

namespace Tenantry.Caching.Internal;

/// <summary>The <see cref="ITenantDistributedCache"/>: the registered distributed cache, under the tenant's prefix.</summary>
internal sealed class TenantDistributedCache(IDistributedCache inner, ICurrentTenant currentTenant) : ITenantDistributedCache
{
    private const string Name = nameof(ITenantDistributedCache);

    public byte[]? Get(string key) => inner.Get(Key(key));

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(Key(key), token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(Key(key), value, options);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        inner.SetAsync(Key(key), value, options, token);

    public void Refresh(string key) => inner.Refresh(Key(key));

    public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(Key(key), token);

    public void Remove(string key) => inner.Remove(Key(key));

    public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(Key(key), token);

    private string Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return currentTenant.Require(Name).Prefix + key;
    }
}
