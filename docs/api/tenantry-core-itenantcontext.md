# `ITenantContext<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Provides read-only access to the currently resolved tenant for the active request scope. Registered as a singleton backed by `AsyncLocal<T>` — the value is per-async-context (effectively per HTTP request) rather than per-instance.

```csharp
public interface ITenantContext<out TKey> where TKey : IEquatable<out TKey>, IParsable<out TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

## Properties

### `CurrentTenant`

The currently resolved tenant, or `null` if no tenant has been resolved (e.g. before the middleware has run, or on anonymous endpoints).

```csharp
ITenantDescriptor<out TKey>? CurrentTenant { get; }
```

Value: [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md)

### `CurrentTenantId`

The current tenant's identifier, or `null` if no tenant is resolved. Equivalent to `CurrentTenant?.TenantId` but exposed as a single property for use in EF Core global query filter expressions — EF Core evaluates single-step member accesses on the `DbContext` per-query, avoiding intermediate object caching.

```csharp
TKey? CurrentTenantId { get; }
```

Value: `TKey`

### `HasTenant`

Returns `true` if a tenant has been resolved for the current scope.

```csharp
bool HasTenant { get; }
```

Value: `bool`
