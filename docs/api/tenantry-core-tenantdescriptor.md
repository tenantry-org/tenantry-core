# `TenantDescriptor<TKey>` class

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Default implementation of [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md).

```csharp
public class TenantDescriptor<TKey> : ITenantDescriptor<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

Implements [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md).

## Properties

### `Name`

Human-readable display name for the tenant.

```csharp
public required string Name { get; init; }
```

Value: `string`

### `TenantId`

Unique tenant identifier used for data isolation.

```csharp
public required TKey TenantId { get; init; }
```

Value: `TKey`
