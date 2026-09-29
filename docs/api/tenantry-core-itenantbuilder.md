# `ITenantBuilder<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Minimal builder interface that captures `TKey` and exposes the service collection. Satellite packages (e.g. Tenantry.EfCore) add extension methods on this interface so users only specify TKey once in `AddTenantry`.

```csharp
public interface ITenantBuilder<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. Must implement `IEquatable<T>` and `IParsable<TSelf>`.

## Properties

### `Services`

Gets the underlying service collection.

```csharp
IServiceCollection Services { get; }
```

Value: `IServiceCollection`

## Methods

### `UseInMemoryStore(IEnumerable<ITenantDescriptor<TKey>>)`

Registers a pre-populated in-memory tenant store.

```csharp
ITenantBuilder<TKey> UseInMemoryStore(IEnumerable<ITenantDescriptor<TKey>> tenants)
```

Parameters:

- `tenants` `IEnumerable<ITenantDescriptor<TKey>>`: The tenants the store holds. The store does not change after registration.

Returns: [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md)

### `UseStore(Func<IServiceProvider, ITenantStore<TKey>>)`

Registers a custom [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) implementation with a factory function.

```csharp
ITenantBuilder<TKey> UseStore(Func<IServiceProvider, ITenantStore<TKey>> factory)
```

Parameters:

- `factory` `Func<IServiceProvider, ITenantStore<TKey>>`: Creates the store from the scope's services.

Returns: [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md)

The store is registered with a **scoped** lifetime and is resolved per operation, so the factory may return an instance that depends on scoped services such as a `DbContext`.

### `UseStore<TStore>()`

Registers a custom [`ITenantStore<TKey>`](tenantry-core-itenantstore.md) implementation.

```csharp
ITenantBuilder<TKey> UseStore<TStore>() where TStore : class, ITenantStore<TKey>
```

Type parameters:

- `TStore`: The store type, created through dependency injection.

Returns: [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md)

The store is registered with a **scoped** lifetime and is resolved per operation — Tenantry creates a fresh scope for singleton/background callers — so the implementation may safely depend on scoped services such as a `DbContext`.
