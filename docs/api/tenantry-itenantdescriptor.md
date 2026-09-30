# `ITenantDescriptor<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Represents a resolved tenant.

```csharp
public interface ITenantDescriptor<out TKey> where TKey : IEquatable<out TKey>, IParsable<out TKey>
```

## Type parameters

- `TKey`: The type used for tenant identifiers (e.g. `Guid`, [string](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/reference-types), [int](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/integral-numeric-types)). Must implement `IEquatable<T>` so EF Core can translate equality checks to SQL, and `IParsable<TSelf>` so middleware can parse the raw string value from HTTP headers/routes.

Derived types: [`TenantDescriptor<TKey>`](tenantry-tenantdescriptor.md).

## Properties

### `Name`

Human-readable display name for the tenant.

```csharp
string Name { get; }
```

Value: `string`

### `TenantId`

Unique tenant identifier used for data isolation.

```csharp
TKey TenantId { get; }
```

Value: `TKey`
