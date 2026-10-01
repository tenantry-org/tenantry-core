# `TenantDescriptor<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Default implementation of [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md).

```csharp
public class TenantDescriptor<TKey> : ITenantDescriptor<TKey>, ITenantDescriptor where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

Implements [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md), [`ITenantDescriptor`](tenantry-itenantdescriptor.md).

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
