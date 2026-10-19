# `TenantIsolationViolationKind` enum

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Which isolation check threw a [`TenantIsolationViolationException`](tenantry-tenantisolationviolationexception.md).

```csharp
public enum TenantIsolationViolationKind
```

## Values

| Value | Description |
|-------|-------------|
| `EntityWrite = 0` | A save would write an entity of another tenant: a new entity that names another tenant, or a changed or deleted entity that was loaded as, or now names, another tenant. |
| `BulkUpdate = 1` | A bulk update, which no save checks, would set `TenantId`, or sets a property the guard cannot identify. In EF Core, an `ExecuteUpdate`. |
| `TenantDatabaseMismatch = 2` | A database-per-tenant context would use a connection that was not set for it (and, pooled, for its current lease) or for the current tenant. |
| `ModelConfiguration = 3` | The model cannot isolate a tenant-owned entity type (for example a missing tenant filter, a base type or owner that is not tenant-owned, or a tenant-owned type mapped to JSON), or, where the integration is set to reject them, has entity types that are neither tenant-owned nor marked as shared. The message names the types and the cause. |
| `SaveWithoutTransaction = 4` | A save would write rows whose tenant check is another of its statements, without a transaction, and the integration is set to reject such a save rather than begin a transaction for it. In EF Core, with `Database.AutoTransactionBehavior` set to `Never`. |
| `TransactionRolledBack = 5` | A save that sent statements in this transaction failed or never reported success, and a save in it wrote rows whose tenant check is another of its statements. The integration could not undo only the failed save (no savepoint, a failed rollback to its savepoint, or an ambient transaction), so the transaction was rolled back instead of committed. |
| `TenantSchemaMismatch = 6` | A schema-per-tenant context would use a schema other than the current tenant's, such as one built for the tenant that was current when it was first used. Thrown by packages that put tenants in schemas of their own, such as Tenantry.Pro. |
