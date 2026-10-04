# `TenantIsolationViolationKind` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Which isolation check threw a [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md).

```csharp
public enum TenantIsolationViolationKind
```

## Values

| Value | Description |
|-------|-------------|
| `EntityWrite = 0` | `SaveChanges` would write an entity of another tenant: a new entity that names another tenant, or a changed or deleted entity that was loaded as, or now names, another tenant. |
| `BulkUpdate = 1` | An `ExecuteUpdate` would set `TenantId`, or sets a property the guard cannot identify. |
| `TenantDatabaseMismatch = 2` | A database-per-tenant context would use a connection that was not set for it (and, pooled, for its current lease) or for the current tenant. |
| `ModelConfiguration = 3` | The model cannot isolate a tenant-owned entity type: for example a missing tenant filter, a base type or owner that is not tenant-owned, or a tenant-owned type mapped to JSON. The message names the type and the cause. |
| `SaveWithoutTransaction = 4` | `SaveChanges` would write rows whose tenant check is another of its statements without a transaction (`Database.AutoTransactionBehavior` is `Never`), and [`EfCoreIsolationOptions.OnSaveWithoutTransaction`](tenantry-efcore-efcoreisolationoptions.md) is [`SaveWithoutTransactionBehavior.Reject`](tenantry-efcore-savewithouttransactionbehavior.md). |
| `TransactionRolledBack = 5` | A save in this transaction failed, or never finished, after sending statements whose tenant check was another of its statements. EF Core could not undo only that save (no savepoint, or an ambient transaction), so the transaction was rolled back instead of committed. |
| `TenantSchemaMismatch = 6` | A schema-per-tenant context would use a schema other than the current tenant's, such as one built for the tenant that was current when it was first used. Thrown by packages that put tenants in schemas of their own, from a [`TenantContextGuard`](tenantry-efcore-tenantcontextguard.md). |
