# `ITenantStore<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Persists and retrieves tenant definitions. Implement this interface to back tenants with a database, configuration file, or any other store.

A custom store registered via `UseStore<TStore>()` is **scoped**, and Tenantry resolves it per operation from a fresh dependency-injection scope (so singleton/background services can read tenants without capturing it). Implementations may therefore depend on scoped services such as a `DbContext`, but must not assume a singleton lifetime or cache scope-bound state across calls.

```csharp
public interface ITenantStore<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

Derived types: [`InMemoryTenantStore<TKey>`](tenantry-core-stores-inmemorytenantstore.md).

## Methods

### `GetAllTenantsAsync(CancellationToken)`

Returns all registered tenants.

```csharp
ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>>`

### `GetTenantAsync(TKey, CancellationToken)`

Returns the tenant with the given `tenantId`, or `null` if no matching tenant exists.

```csharp
ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The identifier of the tenant to find.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`
