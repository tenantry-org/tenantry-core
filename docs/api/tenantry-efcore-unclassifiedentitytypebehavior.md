# `UnclassifiedEntityTypeBehavior` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

What a context does when its model has tenant-owned entity types and also entity types that are neither tenant-owned nor marked as shared across tenants. Set with [`EfCoreIsolationOptions.OnUnclassifiedEntityType`](tenantry-efcore-efcoreisolationoptions.md).

Tenantry isolates only tenant-owned entity types, so the rows of an unclassified one are read and written for every tenant. The check runs once per model, on the context's first query or save. A model with no tenant-owned entity type, such as a database-per-tenant context's, is never checked.

```csharp
public enum UnclassifiedEntityTypeBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `Reject = 0` | Throw [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) of kind [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-efcore-tenantisolationviolationkind.md), naming every unclassified entity type, before the first query or save. The default. |
| `Warn = 1` | As [`UnclassifiedEntityTypeBehavior.Allow`](tenantry-efcore-unclassifiedentitytypebehavior.md), but log a structured warning naming them, once per model. |
| `Allow = 2` | Use the model as it is, silently. |
