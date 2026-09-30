# `TenantEntity<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Optional base class for tenant-owned entities, implementing [`ITenantEntity<TKey>`](tenantry-itenantentity.md).

The setter is public for code that must name the tenant itself, such as seeding or maintenance code that saves without a current tenant. Code that runs as a tenant leaves it unset: new entities are stamped with the current tenant when they are saved.

```csharp
public abstract class TenantEntity<TKey> : ITenantEntity<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantEntity<TKey>`](tenantry-itenantentity.md) for constraints.

Implements [`ITenantEntity<TKey>`](tenantry-itenantentity.md).

## Properties

### `TenantId`

The identifier of the tenant that owns this entity. Set on new entities when they are saved.

```csharp
public TKey TenantId { get; set; }
```

Value: `TKey`
