# `ITenantDescriptor<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Represents a resolved tenant.

Implement it on your own type to carry what your application knows about a tenant (its plan, region or connection string), and return that type from your [`ITenantStore<TKey>`](tenantry-itenantstore.md). Tenantry reads only [`ITenantDescriptor<TKey>.TenantId`](tenantry-itenantdescriptor-1.md) and [`ITenantDescriptor.Name`](tenantry-itenantdescriptor.md); your code reads the rest with [`TenantDescriptorExtensions.As<TTenant>`](tenantry-tenantdescriptorextensions.md) or [`ITenantContext<TKey>.GetCurrentTenant<TTenant>`](tenantry-itenantcontext.md).

```csharp
public interface ITenantDescriptor<out TKey> : ITenantDescriptor where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The type used for tenant identifiers (e.g. `Guid`, [string](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/reference-types), [int](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/integral-numeric-types)). Must implement `IEquatable<T>` so EF Core can translate equality checks to SQL, and `IParsable<TSelf>` so a tenant id can be parsed from text, such as a request's identifier.

Derived types: [`TenantDescriptor<TKey>`](tenantry-tenantdescriptor.md).

## Properties

### `TenantId`

Unique tenant identifier used for data isolation.

```csharp
TKey TenantId { get; }
```

Value: `TKey`
