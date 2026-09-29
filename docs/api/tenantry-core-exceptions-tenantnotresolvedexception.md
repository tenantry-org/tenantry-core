# `TenantNotResolvedException` class

Namespace: `Tenantry.Core.Exceptions` · Package: `Tenantry.Core` · [API reference](README.md)

Thrown when a tenant could not be resolved from the current request context and the operation requires a resolved tenant.

```csharp
public sealed class TenantNotResolvedException : InvalidOperationException, ISerializable
```

Inherits `Exception` → `SystemException` → `InvalidOperationException`.

Implements `ISerializable`.

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
