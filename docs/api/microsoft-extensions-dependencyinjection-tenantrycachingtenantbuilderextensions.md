# `TenantryCachingTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Caching` · [API reference](README.md)

Keeps cached data per tenant.

```csharp
public static class TenantryCachingTenantBuilderExtensions
```

## Methods

### `IsolateCaches<TKey>(ITenantBuilder<TKey>)`

Keys the application's `HybridCache` by tenant: an entry written while a tenant is current is read only while that tenant is current, so one tenant's cached data is never served to another. Entries every tenant shares go through [`SharedHybridCache`](tenantry-caching-sharedhybridcache.md); code that uses `IDistributedCache` directly can inject [`ITenantDistributedCache`](tenantry-caching-itenantdistributedcache.md). Invalidating a tenant ([`ITenantStoreCache<TKey>.Invalidate`](tenantry-itenantstorecache.md)) removes its `HybridCache` entries.

```csharp
public static ITenantBuilder<TKey> IsolateCaches<TKey>(this ITenantBuilder<TKey> builder) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

It wraps the `HybridCache` registered before it, so call `AddHybridCache()` before `AddTenantry`. Without one, the `HybridCache` it registers throws when used, naming the fix, and a later `AddHybridCache()`, which adds a cache only if none is registered, leaves it in place. A `HybridCache` registered later with `AddSingleton` replaces it, unisolated: keep cache registrations before `AddTenantry`.

A `HybridCache` call with no current tenant throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), rather than writing an entry no tenant owns. The tenant's prefix makes keys longer: keep them within the cache's maximum key length (1,024 characters by default) with the id added.

```csharp
builder.Services.AddHybridCache();
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .IsolateCaches());
```
