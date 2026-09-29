# `ITenantStoreAccessor<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Reads tenants from the registered [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) on behalf of singletons, such as hosted services, resolving the store from a fresh dependency-injection scope for each call.

A store registered with `UseStore` is scoped, and may depend on scoped services such as a `DbContext`. Injecting it into a singleton would capture one instance for the life of the application (and fails scope validation in Development). Singletons take this accessor instead, which is correct whatever the store's lifetime. Registered as a singleton by `AddTenantryCore` and `AddTenantry`.

```csharp
public interface ITenantStoreAccessor<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

## Methods

### `GetAllTenantsAsync(CancellationToken)`

Returns all tenants in the store.

```csharp
ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>>`

Exceptions:

- `InvalidOperationException`: No [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) is registered.

### `GetTenantAsync(TKey, CancellationToken)`

Returns the tenant with the given `tenantId`, or `null` if the store has none.

```csharp
ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The identifier of the tenant to find.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`

Exceptions:

- `InvalidOperationException`: No [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) is registered.
