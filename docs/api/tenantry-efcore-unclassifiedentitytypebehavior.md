# `UnclassifiedEntityTypeBehavior` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

What a context does when its model has tenant-owned entity types and also entity types that are neither tenant-owned nor marked as shared across tenants. Set with [`EfCoreIsolationOptions.OnUnclassifiedEntityType`](tenantry-efcore-efcoreisolationoptions.md).

Tenantry isolates only tenant-owned entity types, so the rows of an unclassified one are read and written for every tenant. A context applies its behaviour whenever EF Core compiles one of its queries, and on every save, before anything is read or written. Contexts with different values of it never share a compiled query. A model with no tenant-owned entity type, such as a database-per-tenant context's, is never checked.

```csharp
public enum UnclassifiedEntityTypeBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `Reject = 0` | Throw [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) of kind [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-efcore-tenantisolationviolationkind.md), naming every unclassified entity type, before the first query or save. The default, and what a value outside the enum does. |
| `Warn = 1` | As [`UnclassifiedEntityTypeBehavior.Allow`](tenantry-efcore-unclassifiedentitytypebehavior.md), but log a structured warning naming them, once for each model EF Core builds. |
| `Allow = 2` | Use the model as it is, silently. |
