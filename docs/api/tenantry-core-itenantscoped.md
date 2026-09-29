# `ITenantScoped<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Marker interface for objects that belong to a specific tenant. Implement this interface on any object that the tenant should isolate.

When using the Tenantry EF Core integration, the `TenantId` property will be automatically set by an interceptor on `SaveChanges`. No entity is saved to the wrong tenant. No base class is required.

```csharp
public interface ITenantScoped<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must match the `TKey` used in the rest of the Tenantry registration (e.g. `Guid`, [string](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/reference-types)).

Derived types: [`TenantScoped<TKey>`](tenantry-core-tenantscoped.md).

## Properties

### `TenantId`

The identifier of the tenant that owns this entity. This value is automatically set by the interceptor on `SaveChanges`.

```csharp
TKey TenantId { get; set; }
```

Value: `TKey`
