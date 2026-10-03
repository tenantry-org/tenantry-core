# `ITenantKeyTypeVisitor<TResult>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Code that needs the tenant key type, given it by [`ITenantKeyType`](tenantry-itenantkeytype.md).

```csharp
public interface ITenantKeyTypeVisitor<out TResult>
```

## Type parameters

- `TResult`: What the code returns.

## Methods

### `Visit<TKey>()`

Runs with the tenant key type as `TKey`.

```csharp
TResult Visit<TKey>() where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant key type.

Returns: `TResult`: The result.
