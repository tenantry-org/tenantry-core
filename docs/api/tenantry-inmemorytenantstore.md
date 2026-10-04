# `InMemoryTenantStore<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

An [`ITenantStore<TKey>`](tenantry-itenantstore.md) backed by an in-memory dictionary. Suitable for testing, development, demos, and simple single-instance deployments where tenants do not change at runtime.

```csharp
public sealed class InMemoryTenantStore<TKey> : ITenantStore<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must implement `IEquatable<T>` and `IParsable<TSelf>`.

Implements [`ITenantStore<TKey>`](tenantry-itenantstore.md).

## Constructors

### `InMemoryTenantStore(IEnumerable<ITenantDescriptor<TKey>>)`

Initialises the store with a pre-populated collection of tenants.

```csharp
public InMemoryTenantStore(IEnumerable<ITenantDescriptor<TKey>> tenants)
```

Parameters:

- `tenants` `IEnumerable<ITenantDescriptor<TKey>>`: The tenants the store holds. The store does not change after it is created.

Exceptions:

- `ArgumentNullException`: `tenants` or one of its tenants is null.
- `ArgumentException`: A tenant has an id Tenantry reserves for "no tenant" ([`TenantIds.IsReserved<TKey>`](tenantry-tenantids.md)), two tenants have the same id, or, with `string` ids, two ids differ only in case.

The store finds a tenant by its exact id. It refuses `string` ids that differ only in case because a database whose collation ignores case, the default on SQL Server and MySQL, takes them for one id, so one tenant's query filter would match the other's rows.

## Methods

### `GetAllTenantsAsync(CancellationToken)`

Returns every tenant that exists, suspended or inactive ones included.

```csharp
public ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>>`

### `GetTenantAsync(TKey, CancellationToken)`

Returns the tenant with the given `tenantId`, or `null` if no matching tenant exists.

```csharp
public ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken cancellationToken = default)
```

Parameters:

- `tenantId` `TKey`: The identifier of the tenant to find.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<ITenantDescriptor<TKey>>`
