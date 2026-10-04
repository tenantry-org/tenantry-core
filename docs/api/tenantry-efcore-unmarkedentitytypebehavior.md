# `UnmarkedEntityTypeBehavior` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

What a context does when its model has tenant-owned entity types and also entity types that are neither tenant-owned nor marked as shared across tenants. Set with [`EfCoreIsolationOptions.OnUnmarkedEntityType`](tenantry-efcore-efcoreisolationoptions.md).

An entity type that is not tenant-owned is shared by every tenant, which is how Tenantry is meant to be used. [`UnmarkedEntityTypeBehavior.Warn`](tenantry-efcore-unmarkedentitytypebehavior.md) and [`UnmarkedEntityTypeBehavior.Reject`](tenantry-efcore-unmarkedentitytypebehavior.md) are for an application that wants each such type marked as shared, so a type left without [`ITenantEntity<TKey>`](tenantry-itenantentity.md) by mistake is found. A context applies its behaviour whenever EF Core compiles one of its queries, and on every save, before anything is read or written. Contexts with different values never share a compiled query. A model with no tenant-owned entity type, such as a database-per-tenant context's, is never checked.

```csharp
public enum UnmarkedEntityTypeBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `Allow = 0` | Use the model as it is. The default. |
| `Warn = 1` | Use the model, and log a structured warning naming the unmarked entity types, once for each model EF Core builds. |
| `Reject = 2` | Throw [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) of kind [`TenantIsolationViolationKind.ModelConfiguration`](tenantry-efcore-tenantisolationviolationkind.md), naming the unmarked entity types, before the first query or save. A value outside the enum does the same. |
