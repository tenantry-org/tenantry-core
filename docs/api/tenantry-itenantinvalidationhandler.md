# `ITenantInvalidationHandler<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Clears what an application or a Tenantry package keeps for each tenant when the tenant changes. Every registered handler runs when [`ITenantStoreCache<TKey>.Invalidate`](tenantry-itenantstorecache.md) or [`ITenantStoreCache<TKey>.InvalidateAll`](tenantry-itenantstorecache.md) is called, after the cached tenants are removed, whether or not tenants are cached: Tenantry.Caching's cache entries, Tenantry.AspNetCore's output-cached responses (`IsolateOutputCache()`) and Tenantry.Options' options register one, so one call clears everything Tenantry keeps for a tenant.

Register a handler as a singleton, once: `services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<Guid>, MyHandler>())`. The handlers are resolved the first time a tenant is invalidated, so a handler may depend on [`ITenantStoreCache<TKey>`](tenantry-itenantstorecache.md). Each runs even when another throws; the exception, or an `AggregateException` of several, is thrown once they have all run.

```csharp
public interface ITenantInvalidationHandler<in TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `Invalidate(TKey)`

Clears what is kept for the tenant `tenantId`.

```csharp
void Invalidate(TKey tenantId)
```

Parameters:

- `tenantId` `TKey`: The id of the tenant that changed.

### `InvalidateAll()`

Clears what is kept for every tenant.

```csharp
void InvalidateAll()
```
