# `ITenantStoreCache<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Removes cached tenants, so the next lookup asks the tenant store again. Use it when a tenant changes (it is suspended, renamed or deleted, or its identifiers change) before its cached copy expires.

`AddTenantry` always registers it, as a singleton. Without `tenant.CacheTenants()` nothing is cached, so it has nothing to remove. The cache is in memory, in each instance of the application: invalidating removes the tenant from this instance's cache, and other instances keep their copy until it expires.

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

### `InvalidateAll()`

Removes every tenant from the cache.

```csharp
void InvalidateAll()
```
