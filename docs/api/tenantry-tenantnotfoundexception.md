# `TenantNotFoundException` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Thrown when a tenant is looked up by its id and the tenant store has no tenant with that id, for example by [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md).

It derives from [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), so code that handles that exception handles this one too. Catch it first to treat a message or job for a tenant that no longer exists differently from code that runs without a tenant.

```csharp
public sealed class TenantNotFoundException : TenantNotResolvedException, ISerializable
```

Inherits `Exception` → `SystemException` → `InvalidOperationException` → [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md).

Implements `ISerializable`.

## Constructors

### `TenantNotFoundException(object)`

Initialises a new instance for the tenant id that was not found.

```csharp
public TenantNotFoundException(object tenantId)
```

Parameters:

- `tenantId` `object`: The id that was looked up, which the message formats with the invariant culture.

### `TenantNotFoundException(object, string)`

Initialises a new instance for the tenant id that was not found, with a custom message.

```csharp
public TenantNotFoundException(object tenantId, string message)
```

Parameters:

- `tenantId` `object`: The id that was looked up.
- `message` `string`: The message that describes the error.

## Properties

### `TenantId`

The id that was looked up, of the application's tenant key type.

```csharp
public object TenantId { get; }
```

Value: `object`
