# `MissingTenantBehavior` enum

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

What `SaveChanges` does when it writes tenant-owned entities and no tenant is current. Set with [`EfCoreIsolationOptions.OnMissingTenant`](tenantry-efcore-efcoreisolationoptions.md). Reads are not affected: they always fail closed.

```csharp
public enum MissingTenantBehavior
```

## Values

| Value | Description |
|-------|-------------|
| `Reject = 0` | Throw [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md) before anything is written. The default. |
| `Warn = 1` | As [`MissingTenantBehavior.Allow`](tenantry-efcore-missingtenantbehavior.md), but log a structured warning. Surfaces code that writes without a tenant, such as an endpoint or job that bypassed tenant resolution, without failing it. |
| `Allow = 2` | Save without a tenant, silently. Updates and deletes are then not checked against a tenant, and a new entity is saved only if its `TenantId` is set. For maintenance code that deliberately writes across tenants. |
