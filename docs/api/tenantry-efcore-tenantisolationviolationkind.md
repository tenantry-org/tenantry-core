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
| `ModelConfiguration = 3` | The model does not isolate a tenant-owned entity type: it has no tenant query filter or `TenantId` concurrency token, it uses another tenant key type, or it inherits from or is owned by an entity type that is not tenant-owned; an entity type that is not tenant-owned shares its table; or an owned type's writes cannot be checked through its owner: it has no `TenantId` of its own and a key that does not include its owner's, or it is owned through a key of a tenant-owned type that is neither its primary key nor includes its `TenantId`. |
