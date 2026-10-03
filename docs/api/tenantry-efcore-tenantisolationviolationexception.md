# `TenantIsolationViolationException` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Thrown when EF Core would read or write across tenants: before `SaveChanges` writes another tenant's entity, before an `ExecuteUpdate` that could move rows between tenants, before a pooled database-per-tenant context uses another tenant's database, on the first use of a model that does not isolate a tenant-owned entity type, before a save that must succeed or fail as a whole runs without a transaction it may not begin, or instead of committing a transaction that holds a save whose tenant check failed and could not be undone. Nothing has been written when it is thrown, or, for a commit, kept: the transaction is rolled back. [`TenantIsolationViolationException.Kind`](tenantry-efcore-tenantisolationviolationexception.md) says which.

```csharp
public sealed class TenantIsolationViolationException : InvalidOperationException, ISerializable
```

Inherits `Exception` → `SystemException` → `InvalidOperationException`.

Implements `ISerializable`.

## Constructors

### `TenantIsolationViolationException(TenantIsolationViolationKind, string, string, string?, string?)`

Initialises a new instance.

```csharp
public TenantIsolationViolationException(TenantIsolationViolationKind kind, string typeName, string message, string? offendingTenantId = null, string? expectedTenantId = null)
```

Parameters:

- `kind` [`TenantIsolationViolationKind`](tenantry-efcore-tenantisolationviolationkind.md): Which isolation check failed.
- `typeName` `string`: The CLR type name of the entity, or of the `DbContext` for [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-efcore-tenantisolationviolationkind.md).
- `message` `string`: Why the operation was rejected.
- `offendingTenantId` `string`: The tenant the rejected entity or database belongs to, when known.
- `expectedTenantId` `string`: The current tenant, when known.

## Properties

### `ExpectedTenantId`

The current tenant, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) when none is current or the check does not use it.

```csharp
public string? ExpectedTenantId { get; }
```

Value: `string`

### `Kind`

Which isolation check failed.

```csharp
public TenantIsolationViolationKind Kind { get; }
```

Value: [`TenantIsolationViolationKind`](tenantry-efcore-tenantisolationviolationkind.md)

### `OffendingTenantId`

The tenant the rejected entity or database belongs to, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) when the check does not know one (a bulk update, a model check).

```csharp
public string? OffendingTenantId { get; }
```

Value: `string`

### `TypeName`

The CLR type name of the entity that caused the violation, or of the `DbContext` for [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-efcore-tenantisolationviolationkind.md).

```csharp
public string TypeName { get; }
```

Value: `string`
