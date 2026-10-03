# `EfCoreIsolationOptions` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Options for EF Core tenant isolation, set with `tenant.ConfigureEfCoreIsolation(options => …)`. Every context that uses `UseTenantry()` follows them.

These protections are **always** on, independent of these options: reads fail closed (query filters match nothing when no tenant is resolved); a new entity that names another tenant is rejected; `Modified`/`Deleted` entities must belong to the current tenant, checked before saving and again by the stored tenant in each `UPDATE`/`DELETE`; and `ExecuteUpdate` cannot set `TenantId`. These options govern writes without a tenant. Raw SQL and `IgnoreQueryFilters()` are outside Tenantry's isolation.

```csharp
public sealed class EfCoreIsolationOptions
```

## Properties

### `OnMissingTenant`

What happens when `SaveChanges` writes [`ITenantEntity<TKey>`](tenantry-itenantentity.md) entities without a resolved tenant, or entities those own (EF Core owned types). Saves that write no tenant-owned entity are never affected.

- [`MissingTenantBehavior.Reject`](tenantry-efcore-missingtenantbehavior.md) — throw [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md) before persisting. **Default.**
- [`MissingTenantBehavior.Warn`](tenantry-efcore-missingtenantbehavior.md) — allow the write and log a warning.
- [`MissingTenantBehavior.Allow`](tenantry-efcore-missingtenantbehavior.md) — allow the write silently.

[`MissingTenantBehavior.Warn`](tenantry-efcore-missingtenantbehavior.md) and [`MissingTenantBehavior.Allow`](tenantry-efcore-missingtenantbehavior.md) are for maintenance code that deliberately writes across tenants: updates and deletes are then not tenant-checked, and a new entity must set its `TenantId` explicitly, because an unowned row is always rejected. Reads always fail closed, whatever this setting.

```csharp
public MissingTenantBehavior OnMissingTenant { get; set; }
```

Value: [`MissingTenantBehavior`](tenantry-efcore-missingtenantbehavior.md)

### `OnSaveWithoutTransaction`

What happens when, with `Database.AutoTransactionBehavior` set to `Never` and no transaction, `SaveChanges` writes rows whose tenant check is another of its statements: owned rows in a table of their own, or an entity mapped to more than one table.

- [`SaveWithoutTransactionBehavior.UseTransaction`](tenantry-efcore-savewithouttransactionbehavior.md) — EF Core runs that save in a transaction of its own, as it does by default. **Default.**
- [`SaveWithoutTransactionBehavior.Reject`](tenantry-efcore-savewithouttransactionbehavior.md) — throw [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) before anything is sent.

Without a transaction, a statement sent beside a tenant check that fails would stay written. Saves whose rows each carry their own check are never affected, nor are saves with any other `AutoTransactionBehavior` or in a transaction. A transaction begun on the connection through ADO.NET must be handed to EF Core with `Database.UseTransaction`, or EF Core cannot begin its own and the save fails.

```csharp
public SaveWithoutTransactionBehavior OnSaveWithoutTransaction { get; set; }
```

Value: [`SaveWithoutTransactionBehavior`](tenantry-efcore-savewithouttransactionbehavior.md)
