# `EfCoreIsolationOptions` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Options for EF Core tenant isolation, set with `tenant.ConfigureEfCoreIsolation(options => …)`. Every context that uses `UseTenantry()` follows them.

These protections are **always** on, independent of these options: reads fail closed (query filters match nothing when no tenant is resolved); a new entity that names another tenant is rejected; `Modified`/`Deleted` entities must belong to the current tenant, checked before saving and again by the stored tenant in each `UPDATE`/`DELETE`; and `ExecuteUpdate` cannot set `TenantId`. These options govern writes without a tenant. Raw SQL and `IgnoreQueryFilters()` are outside Tenantry's isolation.

```csharp
public sealed class EfCoreIsolationOptions
```

## Properties

### `OnMissingTenant`

What happens when `SaveChanges` writes [`ITenantEntity<TKey>`](tenantry-itenantentity.md) entities without a resolved tenant. Saves that write no tenant-owned entity are never affected.

- [`MissingTenantBehavior.Reject`](tenantry-efcore-missingtenantbehavior.md) — throw [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md) before persisting. **Default.**
- [`MissingTenantBehavior.Warn`](tenantry-efcore-missingtenantbehavior.md) — allow the write and log a warning.
- [`MissingTenantBehavior.Allow`](tenantry-efcore-missingtenantbehavior.md) — allow the write silently.

[`MissingTenantBehavior.Warn`](tenantry-efcore-missingtenantbehavior.md) and [`MissingTenantBehavior.Allow`](tenantry-efcore-missingtenantbehavior.md) are for maintenance code that deliberately writes across tenants: updates and deletes are then not tenant-checked, and a new entity must set its `TenantId` explicitly, because an unowned row is always rejected. Reads always fail closed, whatever this setting.

```csharp
public MissingTenantBehavior OnMissingTenant { get; set; }
```

Value: [`MissingTenantBehavior`](tenantry-efcore-missingtenantbehavior.md)
