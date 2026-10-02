# `ITenantLookup<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Reads tenants from the registered [`ITenantStore<TKey>`](tenantry-itenantstore.md) on behalf of singletons, such as hosted services, resolving the store from a fresh dependency-injection scope for each call.

A store registered with `UseStore` is scoped, and may depend on scoped services such as a `DbContext`. Injecting it into a singleton would capture one instance for the life of the application (and fails scope validation in Development). Singletons take this lookup instead, which is correct whatever the store's lifetime. Registered as a singleton by `AddTenantry`. Creating it throws `InvalidOperationException` when no store is registered, so a hosted service that depends on it fails as the host starts.

```csharp
public interface ITenantLookup<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `FindByIdentifierAsync(string, CancellationToken)`

Returns the tenant an identifier names, or `null` if it names none, with the store's [`ITenantStore<TKey>.FindByIdentifierAsync`](tenantry-itenantstore.md).

```csharp
ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
```

Parameters:

- `identifier` `string`: The identifier: the tenant's id, or a name the store maps to a tenant.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`

### `GetAllTenantsAsync(CancellationToken)`

Returns all tenants in the store.

```csharp
ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>>`

### `GetTenantAsync(TKey, CancellationToken)`

Returns the tenant with the given `tenantId`, or `null` if the store has none.

```csharp
ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The identifier of the tenant to find.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`
