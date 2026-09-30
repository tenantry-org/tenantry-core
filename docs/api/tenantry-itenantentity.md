# `ITenantEntity<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Marks an entity that belongs to a tenant. Tenantry's EF Core integration filters its reads to the current tenant, stamps new ones with it, and rejects writes to another tenant's.

Only a getter is required: the EF Core integration sets `TenantId` through EF Core's own property access, so the entity may give it a private or init-only setter. Derive from [`TenantEntity<TKey>`](tenantry-tenantentity.md) to get the property with a public setter.

```csharp
public interface ITenantEntity<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must match the `TKey` used in the rest of the Tenantry registration (e.g. `Guid`, [string](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/reference-types)).

Derived types: [`TenantEntity<TKey>`](tenantry-tenantentity.md).

## Properties

### `TenantId`

The identifier of the tenant that owns this entity. Set on new entities when they are saved.

```csharp
TKey TenantId { get; }
```

Value: `TKey`
