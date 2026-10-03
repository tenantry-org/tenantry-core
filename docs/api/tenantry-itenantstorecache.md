# `ITenantStoreCache<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Removes cached tenants and runs every [`ITenantInvalidationHandler<TKey>`](tenantry-itenantinvalidationhandler.md), waiting for them. Call it when a tenant changes or is removed. In asynchronous code, [`ITenantInvalidator<TKey>`](tenantry-itenantinvalidator.md) does the same without blocking.

`AddTenantry` always registers it, as a singleton. Without `tenant.CacheTenants()` no tenants are cached, so it has none to remove, but the invalidation handlers still run. A handler that removes entries from a remote cache blocks the calling thread until it is done. The cache is in memory, in each instance of the application: invalidating removes the tenant from this instance's cache, and other instances keep their copy until it expires.

```csharp
public interface ITenantStoreCache<in TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `Invalidate(TKey)`

Removes the tenant `tenantId`, found by its id or by any identifier, from the cache.

```csharp
void Invalidate(TKey tenantId)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant to remove.

Exceptions:

- `ArgumentNullException`: `tenantId` is null.
- `ArgumentException`: `tenantId` is one Tenantry reserves for "no tenant": the key type's default (`Guid.Empty`, `0`) or an empty string.
- `Exception`: An invalidation handler threw (several: `AggregateException`), after every handler ran.

### `InvalidateAll()`

Removes every tenant from the cache.

```csharp
void InvalidateAll()
```

Exceptions:

- `Exception`: An invalidation handler threw (several: `AggregateException`), after every handler ran.
