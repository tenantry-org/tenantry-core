# `TenantInactiveException` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Thrown when work is to run for a tenant that an [`ITenantActivityValidator<TKey>`](tenantry-itenantactivityvalidator.md) refuses, such as a suspended tenant, for example by [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md).

It derives from [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), so code that handles that exception handles this one too. Catch it first to skip or park work for a suspended tenant rather than treat it as a failure.

```csharp
public sealed class TenantInactiveException : TenantNotResolvedException, ISerializable
```

Inherits `Exception` → `SystemException` → `InvalidOperationException` → [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md).

Implements `ISerializable`.

## Constructors

### `TenantInactiveException(object)`

Initialises a new instance for the tenant that was refused.

```csharp
public TenantInactiveException(object tenantId)
```

Parameters:

- `tenantId` `object`: The tenant's id.

### `TenantInactiveException(object, string)`

Initialises a new instance for the tenant that was refused, with a custom message.

```csharp
public TenantInactiveException(object tenantId, string message)
```

Parameters:

- `tenantId` `object`: The tenant's id.
- `message` `string`: The message that describes the error.

## Properties

### `TenantId`

The tenant's id, of the application's tenant key type.

```csharp
public object TenantId { get; }
```

Value: `object`
