# `TenantConnectionStringProvider<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

The default [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md): calls the configured delegates on every call, without caching.

Public so that other providers (for example a caching one) can wrap it. `UseConnectionStrings` registers it as a singleton, and forwards [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) to it.

```csharp
public sealed class TenantConnectionStringProvider<TKey> : ITenantConnectionStringProvider<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

Implements [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md).

## Constructors

### `TenantConnectionStringProvider(TenantConnectionStringOptions<TKey>)`

The default [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md): calls the configured delegates on every call, without caching.

```csharp
public TenantConnectionStringProvider(TenantConnectionStringOptions<TKey> options)
```

Parameters:

- `options` [`TenantConnectionStringOptions<TKey>`](tenantry-tenantconnectionstringoptions.md): The delegates that return a tenant's connection string.

Public so that other providers (for example a caching one) can wrap it. `UseConnectionStrings` registers it as a singleton, and forwards [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) to it.

## Methods

### `Get(ITenantDescriptor<TKey>)`

Returns `tenant`'s connection string.

```csharp
public string Get(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant whose connection string to return.

Returns: `string`

Exceptions:

- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-tenantconnectionstringoptions.md) is configured (use [`ITenantConnectionStringProvider<TKey>.GetAsync`](tenantry-itenantconnectionstringprovider.md)), or the delegate returned an empty value.

### `GetAsync(ITenantDescriptor<TKey>, CancellationToken)`

Returns `tenant`'s connection string, using the asynchronous delegate if configured.

```csharp
public ValueTask<string> GetAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant whose connection string to return.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<string>`

Exceptions:

- `InvalidOperationException`: The delegate returned an empty value.
