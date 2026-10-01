# `ITenantContextSetter<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Makes a tenant current for the calling code, for code that has already found the tenant and needs no new dependency-injection scope. Most code uses [`ITenantScopeFactory<TKey>`](tenantry-itenantscopefactory.md) instead, which also creates a scope for the tenant's services.

Registered as a singleton by `AddTenantry`. The current tenant is ambient (held in an `AsyncLocal<T>`): it flows into code the caller awaits or starts, never back to the caller's caller.

```csharp
public interface ITenantContextSetter<TKey> : ITenantContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Methods

### `Use(ITenantDescriptor<TKey>)`

Makes `tenant` the current tenant until the returned handle is disposed. Uses may nest: an inner one shadows the outer tenant, and disposing it restores the outer tenant (the outermost one restores "no tenant").

```csharp
IDisposable Use(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant to make current.

Returns: `IDisposable`: A handle that restores the previously current tenant on disposal.

Exceptions:

- `ArgumentException`: The tenant's id is the key type's default value (`Empty`, `0`) or an empty string, which Tenantry reserves for "no tenant".
