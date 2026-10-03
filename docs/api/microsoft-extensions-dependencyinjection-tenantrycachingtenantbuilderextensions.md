# `TenantryCachingTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Caching` · [API reference](README.md)

Keeps cached data per tenant.

```csharp
public static class TenantryCachingTenantBuilderExtensions
```

## Methods

### `IsolateCaches<TKey>(ITenantBuilder<TKey>)`

Keys the application's `HybridCache` by tenant: an entry written while a tenant is current is read only while that tenant is current, so one tenant's cached data is never served to another. Entries every tenant shares go through [`SharedHybridCache`](tenantry-caching-sharedhybridcache.md); code that uses `IDistributedCache` directly can inject [`ITenantDistributedCache`](tenantry-caching-itenantdistributedcache.md). Invalidating a tenant ([`ITenantInvalidator<TKey>.InvalidateAsync`](tenantry-itenantinvalidator.md)) removes its `HybridCache` entries.

```csharp
public static ITenantBuilder<TKey> IsolateCaches<TKey>(this ITenantBuilder<TKey> builder) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: A `HybridCache`, keyed or not, is registered as scoped or transient.

It wraps the `HybridCache` registered before it, so call `AddHybridCache()` before `AddTenantry`. When the host starts, it checks that `HybridCache` resolves to the cache it keys by tenant, and throws `InvalidOperationException` otherwise: for a `HybridCache`, keyed or not, registered after it (which would replace it), for one registered for any key, and for `AddHybridCache()` called after it. With no `HybridCache` registered at all, the one it registers throws when used, naming the fix.

A keyed `HybridCache` registered before it is kept per tenant the same way, and the same key gives a [`SharedHybridCache`](tenantry-caching-sharedhybridcache.md) for that cache's shared entries.

A `HybridCache` call with no current tenant throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), rather than writing an entry no tenant owns. The tenant's prefix makes keys longer: keep them within the cache's maximum key length (1,024 characters by default) with the id added.

`AddHybridCache()` is in the Microsoft.Extensions.Caching.Hybrid package.

```csharp
builder.Services.AddHybridCache();
builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<AppTenantStore>()
    .IsolateCaches());
```
