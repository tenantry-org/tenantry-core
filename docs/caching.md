# Caching per tenant

A cache keyed only by what the code asks for (`"orders:recent"`) serves one tenant's data to another. `Tenantry.Caching`
keeps `HybridCache` entries per tenant, and `Tenantry.AspNetCore` does the same for the output cache, so the code that
reads and writes the cache does not have to name the tenant.

```bash
dotnet add package Tenantry.Caching
```

## HybridCache

Register the cache, then `IsolateCaches()` in `AddTenantry`:

```csharp
builder.Services.AddHybridCache();
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .IsolateCaches());
```

Code that injects `HybridCache` is unchanged, and its entries are now the current tenant's:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

public sealed class RecentOrders(HybridCache cache, AppDbContext db)
{
    public ValueTask<List<Order>> GetAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync("orders:recent", async token =>
            await db.Orders.OrderByDescending(o => o.CreatedAt).Take(20).ToListAsync(token), cancellationToken: ct);
}
```

- **Keys and tags are the tenant's.** An entry written while Acme is current is read only while Acme is current.
  `RemoveAsync` and `RemoveByTagAsync` reach only the current tenant's entries.
- **The factory runs as the tenant.** Microsoft's `HybridCache` runs a factory without the caller's async context,
  where the current tenant lives, so without Tenantry a factory that queries a tenant's `DbContext` would run with no
  tenant. `IsolateCaches()` makes the calling tenant current while the factory runs.
- **No tenant, no cache.** A call with no current tenant throws `TenantNotResolvedException`, rather than reading or
  writing an entry no tenant owns.
- **Register the cache first.** `IsolateCaches()` wraps the `HybridCache` registered before it, so call
  `AddHybridCache()` before `AddTenantry`. Without one, the `HybridCache` it registers throws when used, naming the fix.
- **Keys get longer.** Each key carries the tenant's id, so keep keys within the cache's maximum key length (1,024
  characters by default) with the id added.

### Entries every tenant shares

Inject `SharedHybridCache` where an entry is meant for every tenant, so the constructor says so:

```csharp
using Tenantry.Caching;

public sealed class ExchangeRates(SharedHybridCache cache)
{
    public ValueTask<decimal> GetAsync(string currency, CancellationToken ct) =>
        cache.GetOrCreateAsync($"fx:{currency}", _ => ValueTask.FromResult(1.17m), cancellationToken: ct);
}
```

It works with or without a current tenant, and its keys and tags never name a tenant's entry. Its factory runs with no
current tenant, so load data that no tenant owns.

### IDistributedCache

`IsolateCaches()` leaves `IDistributedCache` as it is: framework components use it outside any tenant (session state,
and `HybridCache`'s own second level, whose keys already carry the tenant), so isolating it everywhere would break them.
Code that uses `IDistributedCache` directly for a tenant's data injects `ITenantDistributedCache` instead, the same
cache with each key under the current tenant's prefix:

```csharp
using Microsoft.Extensions.Caching.Distributed;

public sealed class DraftStore(ITenantDistributedCache cache)
{
    public Task SaveAsync(string id, byte[] draft, CancellationToken ct) =>
        cache.SetAsync($"draft:{id}", draft, new DistributedCacheEntryOptions(), ct);
}
```

## Output caching

`IsolateOutputCache()`, in `Tenantry.AspNetCore`, makes every cached response vary by the request's tenant:

```csharp
builder.Services.AddOutputCache();
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .IsolateOutputCache());

var app = builder.Build();
app.UseTenantry();
app.UseOutputCache();   // after UseTenantry()

app.MapGet("/catalogue", () => "…").CacheOutput();
```

The output cache must come after `app.UseTenantry()`, so the tenant is known when it runs. In the other order, a
request the output cache handles throws, naming the fix, rather than being cached for every tenant. A response for a
request without a tenant (an endpoint with `AllowMissingTenant()`) is cached apart from every tenant's.

## Invalidating a tenant

`ITenantStoreCache<TKey>.Invalidate(tenantId)` removes the tenant's `HybridCache` entries (by a tag every tenant entry
carries) and evicts its cached responses, along with its cached descriptor; `InvalidateAll()` does it for every
tenant, and leaves shared entries. Call it when a tenant changes or is removed (see
[Tenant stores](tenant-stores.md#everything-kept-for-a-tenant)).

- **HybridCache** records a tag's invalidation in its second level, when it has one, so other instances of the
  application see it too. Without one, each instance has its own entries.
- **Output caching** in memory (the default store) is per instance; a distributed store shares the eviction.
- **`ITenantDistributedCache`** entries cannot be removed by tag: they expire.

## See also

- [Tenant stores](tenant-stores.md) — caching tenants, and invalidation handlers of your own
- [AOT & trimming](aot-and-trimming.md)
