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
| `TenantDatabaseMismatch = 2` | A pooled database-per-tenant context would use a connection that was not set for its current lease and the current tenant. |
| `ModelConfiguration = 3` | The model does not isolate a tenant-owned entity type: it has no tenant query filter or `TenantId` concurrency token, it uses another tenant key type, or it inherits from or is owned by an entity type that is not tenant-owned; an entity type that is not tenant-owned shares its table; or an owned type's writes cannot be checked through its owner: it has no `TenantId` of its own and a key that does not include its owner's, it is owned through a key of a tenant-owned type that is neither its primary key nor includes its `TenantId`, or it is tenant-owned and mapped to JSON. |
| `SaveWithoutTransaction = 4` | `SaveChanges` would write rows whose tenant check is another of its statements without a transaction (`Database.AutoTransactionBehavior` is `Never`), and [`EfCoreIsolationOptions.OnSaveWithoutTransaction`](tenantry-efcore-efcoreisolationoptions.md) is [`SaveWithoutTransactionBehavior.Reject`](tenantry-efcore-savewithouttransactionbehavior.md). |
| `TransactionRolledBack = 5` | A transaction was about to commit, or an ambient one to complete, holding a `SaveChanges` that failed, or did not end, after sending some of its statements, among them rows whose tenant another of its statements checks, and EF Core could not undo that save in it (no savepoint, or an ambient transaction). It was rolled back instead. |
