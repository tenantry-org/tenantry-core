# `TenantNotResolvedException` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Thrown when an operation needs a current tenant and none is current, or when the tenant it names does not exist ([`TenantNotFoundException`](tenantry-tenantnotfoundexception.md)).

```csharp
public class TenantNotResolvedException : InvalidOperationException, ISerializable
```

Inherits `Exception` → `SystemException` → `InvalidOperationException`.

Implements `ISerializable`.

Derived types: [`TenantNotFoundException`](tenantry-tenantnotfoundexception.md).

## Constructors

### `TenantNotResolvedException()`

Initialises a new instance with a default message.

```csharp
public TenantNotResolvedException()
```

### `TenantNotResolvedException(string)`

Initialises a new instance with a custom message.

```csharp
public TenantNotResolvedException(string message)
```

Parameters:

- `message` `string`: The message that describes the error.

### `TenantNotResolvedException(string, Exception)`

Initialises a new instance with a custom message and inner exception.

```csharp
public TenantNotResolvedException(string message, Exception innerException)
```

Parameters:

- `message` `string`: The message that describes the error.
- `innerException` `Exception`: The exception that caused this one.
