# `EfCoreIsolationOptions` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Options for EF Core tenant isolation: the application's, set with `tenant.ConfigureEfCoreIsolation(options => …)`, or one context's, set with `options.UseTenantry(o => …)`.

Reads always fail closed, a new entity that names another tenant is always rejected, and `ExecuteUpdate` can never set `TenantId`. Whenever a tenant is current, `Modified` and `Deleted` entities must belong to it, checked before saving and again in each `UPDATE` and `DELETE`. These options decide what happens to writes without a tenant, to saves without a transaction, and to entity types neither tenant-owned nor shared. `Database.SqlQuery`, `ExecuteSql` and `IgnoreQueryFilters()` are outside Tenantry's isolation.

```csharp
public sealed class EfCoreIsolationOptions
```

## Properties

### `OnMissingTenant`

What a save does when it writes tenant-owned entities and no tenant is current. Defaults to [`MissingTenantBehavior.Reject`](tenantry-efcore-missingtenantbehavior.md). Saves that write no tenant-owned entity are never affected.

```csharp
public MissingTenantBehavior OnMissingTenant { get; set; }
```

Value: [`MissingTenantBehavior`](tenantry-efcore-missingtenantbehavior.md)

### `OnSaveWithoutTransaction`

What a save does when `AutoTransactionBehavior` is `Never` and some of its rows depend on another statement's tenant check (owned rows in their own table, an entity split across tables, or many-to-many join rows). Defaults to [`SaveWithoutTransactionBehavior.UseTransaction`](tenantry-efcore-savewithouttransactionbehavior.md).

```csharp
public SaveWithoutTransactionBehavior OnSaveWithoutTransaction { get; set; }
```

Value: [`SaveWithoutTransactionBehavior`](tenantry-efcore-savewithouttransactionbehavior.md)

### `OnUnmarkedEntityType`

What a context does when its model has tenant-owned entity types and also entity types that are neither tenant-owned nor marked as shared across tenants. Defaults to [`UnmarkedEntityTypeBehavior.Allow`](tenantry-efcore-unmarkedentitytypebehavior.md).

```csharp
public UnmarkedEntityTypeBehavior OnUnmarkedEntityType { get; set; }
```

Value: [`UnmarkedEntityTypeBehavior`](tenantry-efcore-unmarkedentitytypebehavior.md)

Mark an entity type whose rows every tenant shares with [`SharedAcrossTenantsAttribute`](tenantry-efcore-sharedacrosstenantsattribute.md) or `IsSharedAcrossTenants()`. [`TenantModel.FindUnisolatedEntityTypes`](tenantry-efcore-tenantmodel.md) lists the types this checks.
