# `TenantIsolationViolationException` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Thrown when a data access integration would read or write across tenants. [`TenantIsolationViolationException.Kind`](tenantry-tenantisolationviolationexception.md) says which check failed. Nothing has been written; a refused commit is rolled back.

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

- `kind` [`TenantIsolationViolationKind`](tenantry-tenantisolationviolationkind.md): Which isolation check failed.
- `typeName` `string`: The CLR type name of the entity the check concerns, or of the context for a check of the whole context.
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

Tenantry.EfCore sets it for [`TenantIsolationViolationKind.EntityWrite`](tenantry-tenantisolationviolationkind.md), and for [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-tenantisolationviolationkind.md) when a tenant is current. It is [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for every other kind Tenantry.EfCore throws. Tenantry.Pro sets it for the [`TenantIsolationViolationKind.TenantSchemaMismatch`](tenantry-tenantisolationviolationkind.md) it throws, and for the [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-tenantisolationviolationkind.md) it throws for a Shared tenant's context.

### `Kind`

Which isolation check failed.

```csharp
public TenantIsolationViolationKind Kind { get; }
```

Value: [`TenantIsolationViolationKind`](tenantry-tenantisolationviolationkind.md)

### `OffendingTenantId`

The tenant the rejected entity or database belongs to, as a string for logging, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null).

```csharp
public string? OffendingTenantId { get; }
```

Value: `string`

Tenantry.EfCore sets it for [`TenantIsolationViolationKind.EntityWrite`](tenantry-tenantisolationviolationkind.md), except when it could not read the row's tenant: a join row saved without the tenant-owned row it joins, an owned entity saved without its owner, or an entity with no row stored for the current tenant. It also sets it for [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-tenantisolationviolationkind.md) when the context was connected to a tenant's database. It is [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for every other kind Tenantry.EfCore throws, and for those Tenantry.Pro throws.

### `TypeName`

The CLR type name of the entity the check concerns, or of the context (in EF Core, the `DbContext`) for a check of the whole context: [`TenantIsolationViolationKind.TenantDatabaseMismatch`](tenantry-tenantisolationviolationkind.md), [`TenantIsolationViolationKind.TenantSchemaMismatch`](tenantry-tenantisolationviolationkind.md), [`TenantIsolationViolationKind.SaveWithoutTransaction`](tenantry-tenantisolationviolationkind.md), [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-tenantisolationviolationkind.md) for unmarked entity types, and [`TenantIsolationViolationKind.TransactionRolledBack`](tenantry-tenantisolationviolationkind.md) when no single entity's check failed.

```csharp
public string TypeName { get; }
```

Value: `string`
