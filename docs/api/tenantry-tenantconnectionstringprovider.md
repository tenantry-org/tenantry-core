# `TenantConnectionStringProvider<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

An extension point: for code that extends the package, such as another package that builds on it. An application rarely needs it.

The default [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md): calls the configured delegates on every call, without caching.

Public so that other providers (for example a caching one) can wrap it. `UseConnectionStrings` registers it as a singleton, and forwards [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) to it.

```csharp
[EditorBrowsable(EditorBrowsableState.Advanced)]
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

## Properties

### `CanGetSynchronously`

Whether [`ITenantConnectionStringProvider<TKey>.Get`](tenantry-itenantconnectionstringprovider.md) can return connection strings. When it cannot, Tenantry.EfCore's scoped database-per-tenant context reads its connection string with [`ITenantConnectionStringProvider<TKey>.GetAsync`](tenantry-itenantconnectionstringprovider.md) when it first opens a connection, so only asynchronous EF Core calls work on it. A decorator should return its inner provider's value.

```csharp
public bool CanGetSynchronously { get; }
```

Value: `bool`: By default, [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool).

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
