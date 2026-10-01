# `ITenantConnectionStringProvider<TKey>` interface

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Returns a tenant's connection string, as configured by [`TenantConnectionStringOptions<TKey>`](tenantry-tenantconnectionstringoptions.md).

Registered as a singleton by `UseConnectionStrings`. It takes the tenant explicitly, for code such as migration runners that visits tenants without making each one current. For the current tenant's connection string, use [`CurrentTenantConnectionString<TKey>`](tenantry-currenttenantconnectionstring.md).

Implement it to decorate the default [`TenantConnectionStringProvider<TKey>`](tenantry-tenantconnectionstringprovider.md), for example to cache.

```csharp
public interface ITenantConnectionStringProvider<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

Derived types: [`TenantConnectionStringProvider<TKey>`](tenantry-tenantconnectionstringprovider.md).

## Methods

### `Get(ITenantDescriptor<TKey>)`

Returns `tenant`'s connection string.

```csharp
string Get(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant whose connection string to return.

Returns: `string`

Exceptions:

- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-tenantconnectionstringoptions.md) is configured (use [`ITenantConnectionStringProvider<TKey>.GetAsync`](tenantry-itenantconnectionstringprovider.md)), or the delegate returned an empty value.

### `GetAsync(ITenantDescriptor<TKey>, CancellationToken)`

Returns `tenant`'s connection string, using the asynchronous delegate if configured.

```csharp
ValueTask<string> GetAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md): The tenant whose connection string to return.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<string>`

Exceptions:

- `InvalidOperationException`: The delegate returned an empty value.
