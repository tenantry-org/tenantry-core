# `TenantIsolationViolationException` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Thrown when EF Core would read or write across tenants. [`TenantIsolationViolationException.Kind`](tenantry-efcore-tenantisolationviolationexception.md) says which check failed. Nothing has been written; a refused commit is rolled back.

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
- `typeName` `string`: The CLR type name of the entity the check concerns, or of the `DbContext` for a check of the whole context.
- `message` `string`: Why the operation was rejected.
- `offendingTenantId` `string`: The tenant the rejected entity or database belongs to, when known.
- `expectedTenantId` `string`: The current tenant, when known.

## Properties

### `ExpectedTenantId`

The current tenant, as a string for logging, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null).

```csharp
public string? ExpectedTenantId { get; }
```

Value: `string`

Tenantry sets it for [`TenantIsolationViolationKind.EntityWrite`](tenantry-efcore-tenantisolationviolationkind.md), and for [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-efcore-tenantisolationviolationkind.md) when a tenant is current. It is [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for every other kind Tenantry throws. Tenantry.Pro sets it for the [`TenantIsolationViolationKind.TenantSchemaMismatch`](tenantry-efcore-tenantisolationviolationkind.md) it throws, and for the [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-efcore-tenantisolationviolationkind.md) it throws for a Shared tenant's context.

### `Kind`

Which isolation check failed.

```csharp
public TenantIsolationViolationKind Kind { get; }
```

Value: [`TenantIsolationViolationKind`](tenantry-efcore-tenantisolationviolationkind.md)

### `OffendingTenantId`

The tenant the rejected entity or database belongs to, as a string for logging, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null).

```csharp
public string? OffendingTenantId { get; }
```

Value: `string`

Tenantry sets it for [`TenantIsolationViolationKind.EntityWrite`](tenantry-efcore-tenantisolationviolationkind.md), except when it could not read the row's tenant: a join row saved without the tenant-owned row it joins, an owned entity saved without its owner, or an entity with no row stored for the current tenant. It also sets it for [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-efcore-tenantisolationviolationkind.md) when the context was connected to a tenant's database. It is [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for every other kind Tenantry throws, and for those Tenantry.Pro throws.

### `TypeName`

The CLR type name of the entity the check concerns, or of the `DbContext` for a check of the whole context: [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-efcore-tenantisolationviolationkind.md), [`TenantIsolationViolationKind.TenantSchemaMismatch`](tenantry-efcore-tenantisolationviolationkind.md), [`TenantIsolationViolationKind.SaveWithoutTransaction`](tenantry-efcore-tenantisolationviolationkind.md), [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-efcore-tenantisolationviolationkind.md) for unmarked entity types, and [`TenantIsolationViolationKind.TransactionRolledBack`](tenantry-efcore-tenantisolationviolationkind.md) when no single entity's check failed.

```csharp
public string TypeName { get; }
```

Value: `string`
