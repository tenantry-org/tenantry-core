# `EfCoreIsolationOptions` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Options for configuring EF Core tenant isolation registration. Passed to `builder.AddEfCoreIsolation(options => ...)`.

These protections are **always** on, independent of these options: reads fail closed (query filters match nothing when no tenant is resolved); `Modified`/`Deleted` entities must belong to the current tenant, checked before saving and again by the stored tenant in each `UPDATE`/`DELETE`; and `ExecuteUpdate` cannot set `TenantId`. These options govern writes without a tenant and inserts that name another tenant. Raw SQL and `IgnoreQueryFilters()` are outside Tenantry's isolation.

```csharp
public sealed class EfCoreIsolationOptions
```

## Properties

### `DetectSpoofedWrites`

When [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool), an `Added` entity that carries an explicitly-set `TenantId` belonging to a tenant other than the current one throws [`TenantIsolationViolationException`](tenantry-core-exceptions-tenantisolationviolationexception.md) before any data is written (spoofing detection). When [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) (the default), such a value is silently overwritten with the current tenant by the stamping interceptor.

```csharp
public bool DetectSpoofedWrites { get; set; }
```

Value: `bool`

### `OnMissingTenant`

What happens when `SaveChanges` writes [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) entities without a resolved tenant. Saves that write no tenant-scoped entity are never affected.

- [`MissingTenantBehavior.Reject`](tenantry-core-missingtenantbehavior.md) — throw [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md) before persisting. **Default.**
- [`MissingTenantBehavior.Warn`](tenantry-core-missingtenantbehavior.md) — allow the write and log a warning.
- [`MissingTenantBehavior.Allow`](tenantry-core-missingtenantbehavior.md) — allow the write silently.

[`MissingTenantBehavior.Warn`](tenantry-core-missingtenantbehavior.md) and [`MissingTenantBehavior.Allow`](tenantry-core-missingtenantbehavior.md) are for maintenance code that deliberately writes across tenants: updates and deletes are then not tenant-checked, and a new entity must set its `TenantId` explicitly, because an unowned row is always rejected. Reads always fail closed, whatever this setting.

```csharp
public MissingTenantBehavior OnMissingTenant { get; set; }
```

Value: [`MissingTenantBehavior`](tenantry-core-missingtenantbehavior.md)

Exceptions:

- `ArgumentOutOfRangeException`: The value is [`MissingTenantBehavior.Skip`](tenantry-core-missingtenantbehavior.md) (which only applies to background-job propagation) or not a defined value.
