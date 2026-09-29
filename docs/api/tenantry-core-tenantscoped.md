# `TenantScoped<TKey>` class

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Optional base class for tenant-owned objects. Implements [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) for convenience.

Using this base class is not required — you can implement [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) directly. However, if you do use it, the `TenantId` property will be automatically implemented.

```csharp
public abstract class TenantScoped<TKey> : ITenantScoped<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) for constraints.

Implements [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md).

## Properties

### `TenantId`

The identifier of the tenant that owns this entity. This value is automatically set by the interceptor on `SaveChanges`.

```csharp
public TKey TenantId { get; set; }
```

Value: `TKey`
