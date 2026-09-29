# `TenantConnectionStringResolver<TKey>` class

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

The default [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md): calls the configured delegates on every resolution, without caching.

Public so that other resolvers (for example a caching one) can wrap it. `UseConnectionStrings` registers it as a singleton, and forwards [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md) to it.

```csharp
public sealed class TenantConnectionStringResolver<TKey> : ITenantConnectionStringResolver<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

Implements [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md).

## Constructors

### `TenantConnectionStringResolver(ITenantContext<TKey>, TenantConnectionStringOptions<TKey>)`

The default [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md): calls the configured delegates on every resolution, without caching.

```csharp
public TenantConnectionStringResolver(ITenantContext<TKey> tenantContext, TenantConnectionStringOptions<TKey> options)
```

Parameters:

- `tenantContext` [`ITenantContext<TKey>`](tenantry-core-itenantcontext.md): Supplies the current tenant.
- `options` [`TenantConnectionStringOptions<TKey>`](tenantry-core-tenantconnectionstringoptions.md): The delegates that return a tenant's connection string.

Public so that other resolvers (for example a caching one) can wrap it. `UseConnectionStrings` registers it as a singleton, and forwards [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md) to it.

## Methods

### `Resolve()`

Returns the current tenant's connection string.

```csharp
public string Resolve()
```

Returns: `string`

Exceptions:

- [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md): No tenant is current.
- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-core-tenantconnectionstringoptions.md) is configured (use [`ITenantConnectionStringResolver<TKey>.ResolveAsync`](tenantry-core-itenantconnectionstringresolver.md)), or the delegate returned an empty value.

### `Resolve(ITenantDescriptor<TKey>)`

Returns `tenant`'s connection string.

```csharp
public string Resolve(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md): The tenant whose connection string to return.

Returns: `string`

Exceptions:

- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-core-tenantconnectionstringoptions.md) is configured (use [`ITenantConnectionStringResolver<TKey>.ResolveAsync`](tenantry-core-itenantconnectionstringresolver.md)), or the delegate returned an empty value.

### `ResolveAsync(CancellationToken)`

Returns the current tenant's connection string, using the asynchronous delegate if configured.

```csharp
public ValueTask<string> ResolveAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<string>`

Exceptions:

- [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md): No tenant is current.
- `InvalidOperationException`: The delegate returned an empty value.

### `ResolveAsync(ITenantDescriptor<TKey>, CancellationToken)`

Returns `tenant`'s connection string, using the asynchronous delegate if configured.

```csharp
public ValueTask<string> ResolveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md): The tenant whose connection string to return.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<string>`

Exceptions:

- `InvalidOperationException`: The delegate returned an empty value.
