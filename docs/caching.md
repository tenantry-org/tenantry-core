# Caching per tenant

A cache keyed only by what the code asks for (`"orders:recent"`) serves one tenant's data to another. `Tenantry.Caching`
keeps `HybridCache` entries per tenant, and `Tenantry.AspNetCore` does the same for the output cache, so code that
reads and writes the cache need not name the tenant.

```bash
dotnet add package Tenantry.Caching
dotnet add package Microsoft.Extensions.Caching.Hybrid   # AddHybridCache(), unless you already register a HybridCache
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

- An entry written while Acme is current is read only while Acme is current. `RemoveAsync` and `RemoveByTagAsync`
  reach only the current tenant's entries, and the tag `*` means every entry of the current tenant.
- The factory runs as the calling tenant. Microsoft's `HybridCache` can run a factory outside the caller's async
  context, where the current tenant lives (it does when the call's token can be cancelled), so without Tenantry a
  factory that queries a tenant's `DbContext` could run with no tenant.
- A call with no current tenant throws `TenantNotResolvedException`, rather than reading or writing an entry no tenant
  owns.
- `IsolateCaches()` wraps the `HybridCache` registered before it, so call `AddHybridCache()` (or another library's
  registration of a `HybridCache`) before `AddTenantry`. If a `HybridCache`, keyed or not, is registered after
  `AddTenantry`, the host does not start (`InvalidOperationException`). A service provider built without a host is not
  checked.
- The cache must be a singleton, as `AddHybridCache()` registers it: `IsolateCaches()` throws for a `HybridCache`,
  keyed or not, registered as scoped or transient, as invalidating a tenant clears the cache outside any scope.
- A keyed `HybridCache` registered before `AddTenantry` is kept per tenant the same way:
  `[FromKeyedServices("reports")] HybridCache` holds the current tenant's entries, and
  `[FromKeyedServices("reports")] SharedHybridCache` holds entries every tenant shares in that cache. Invalidating a
  tenant clears its entries from each one. A `HybridCache` registered for any key (`KeyedService.AnyKey`) stops the
  host, since its keys are not known in advance to clear.
- Each key carries the tenant's id, so keep keys within the cache's maximum key length (1,024 characters by default)
  with the id added.

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

It works with or without a current tenant, and its keys and tags never name a tenant's entry; `*` means every shared
entry. Its factory always runs with no current tenant, on the thread pool, whoever calls: a query through a tenant's
`DbContext` there fails, as it does outside a tenant, rather than caching one tenant's rows for every tenant.

### IDistributedCache

`IsolateCaches()` leaves `IDistributedCache` as it is, as framework components use it outside any tenant (session state,
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

### IMemoryCache

Tenantry does not isolate `IMemoryCache`: a tenant's data cached there under a key without the tenant is read by every
tenant. Use `HybridCache`, which `IsolateCaches()` isolates, or put the tenant id in the key.

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

The output cache must come after `app.UseTenantry()`, so the tenant is known when it runs. A response for a request
`app.UseTenantry()` did not handle first (the other order, or a branch of the pipeline without it) is not cached, and a
warning says so once (event 1009). A response for a request without a tenant (an endpoint with `AllowMissingTenant()`)
is cached apart from every tenant's.

## Invalidating a tenant

`ITenantInvalidator<TKey>.InvalidateAsync(tenantId)` removes the tenant's `HybridCache` entries (by a tag every tenant
entry carries) and evicts its cached responses, along with its cached descriptor; `InvalidateAllAsync()` does it for
every tenant, and leaves shared entries. Call it when a tenant changes or is removed
([Tenant stores](tenant-stores.md#everything-kept-for-a-tenant)).

- It takes effect on the instance that calls it. Microsoft's `HybridCache` marks the tag invalid in its second level,
  but each instance keeps the invalidation times it has already read, so other instances of the application serve
  their copies until the entries expire, unless you
  [publish the invalidation to them](tenant-stores.md#several-instances). Otherwise keep entries short-lived
  (`HybridCacheEntryOptions.Expiration` and `LocalCacheExpiration`), as with the tenant cache.
- The output cache's in-memory store (the default) is per instance too; a store shared between instances shares the
  eviction.
- `ITenantDistributedCache` entries cannot be removed by tag: they expire.

## See also

- [Tenant stores](tenant-stores.md): caching tenants, and invalidation handlers of your own
- [AOT & trimming](aot-and-trimming.md)
