# `ITenantBuilder<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The builder `AddTenantry<TKey>` passes to its configuration callback. Tenantry's features are extension methods on it that return it, so calls chain.

```csharp
public interface ITenantBuilder<TKey> : ITenantBuilder where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must implement `IEquatable<T>` and `IParsable<TSelf>`.

## Methods

### `UseStore<TStore>()`

Registers a custom [`ITenantStore<TKey>`](tenantry-itenantstore.md) implementation.

```csharp
ITenantBuilder<TKey> UseStore<TStore>() where TStore : class, ITenantStore<TKey>
```

Type parameters:

- `TStore`: The store type, created through dependency injection.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md)

Exceptions:

- `InvalidOperationException`: A tenant store is already registered.

The store is scoped and read through [`ITenantLookup<TKey>`](tenantry-itenantlookup.md), which resolves it from a new scope for each lookup, so it may depend on scoped services such as a `DbContext`.
