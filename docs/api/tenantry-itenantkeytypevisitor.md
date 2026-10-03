# `ITenantKeyTypeVisitor<TResult>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

An extension point: for code that extends the package, such as another package that builds on it. An application rarely needs it.

Code that needs the tenant key type, given it by [`ITenantKeyType`](tenantry-itenantkeytype.md).

```csharp
[EditorBrowsable(EditorBrowsableState.Advanced)]
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
