# `ITenantStoreCache<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Removes cached tenants, so the next lookup asks the tenant store again, and clears what else is kept for them: each registered [`ITenantInvalidationHandler<TKey>`](tenantry-itenantinvalidationhandler.md) runs too (Tenantry.Caching's cache entries, Tenantry.AspNetCore's output-cached responses, Tenantry.Options' options). Use it when a tenant changes (it is suspended, renamed or deleted, or its identifiers or settings change) before its cached copy expires, and when a tenant is removed.

`AddTenantry` always registers it, as a singleton. Without `tenant.CacheTenants()` no tenants are cached, so it has none to remove, but the invalidation handlers still run. The cache is in memory, in each instance of the application: invalidating removes the tenant from this instance's cache, and other instances keep their copy until it expires.

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
