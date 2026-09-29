# `TenantIsolationViolationException` class

Namespace: `Tenantry.Core.Exceptions` · Package: `Tenantry.Core` · [API reference](README.md)

Thrown when a cross-tenant data isolation violation is detected during a `SaveChanges` or `SaveChangesAsync` call, or when a bulk update would set `TenantId`. This exception is raised *before* any changes are written to the database.

```csharp
public sealed class TenantIsolationViolationException : InvalidOperationException, ISerializable
```

Inherits `Exception` → `SystemException` → `InvalidOperationException`.

Implements `ISerializable`.

## Constructors

### `TenantIsolationViolationException(string, string)`

Initialises a new instance for a violation detected before any tenant value is known, such as a bulk update that would set `TenantId`. [`TenantIsolationViolationException.OffendingTenantId`](tenantry-core-exceptions-tenantisolationviolationexception.md) and [`TenantIsolationViolationException.ExpectedTenantId`](tenantry-core-exceptions-tenantisolationviolationexception.md) are empty.

```csharp
public TenantIsolationViolationException(string entityTypeName, string message)
```

Parameters:

- `entityTypeName` `string`: CLR type name of the violating entity.
- `message` `string`: Why the operation was rejected.

### `TenantIsolationViolationException(string, string, string)`

Initialises a new instance with full diagnostic context.

```csharp
public TenantIsolationViolationException(string entityTypeName, string offendingTenantId, string expectedTenantId)
```

Parameters:

- `entityTypeName` `string`: CLR type name of the violating entity.
- `offendingTenantId` `string`: TenantId found on the entity.
- `expectedTenantId` `string`: TenantId of the current tenant scope.

## Properties

### `EntityTypeName`

The CLR type name of the entity that caused the violation.

```csharp
public string EntityTypeName { get; }
```

Value: `string`

### `ExpectedTenantId`

The `TenantId` of the currently active tenant scope.

```csharp
public string ExpectedTenantId { get; }
```

Value: `string`

### `OffendingTenantId`

The `TenantId` value found on the offending entity.

```csharp
public string OffendingTenantId { get; }
```

Value: `string`
