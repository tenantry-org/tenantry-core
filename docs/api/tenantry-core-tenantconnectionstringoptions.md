# `TenantConnectionStringOptions<TKey>` class

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

How to find each tenant's connection string, for applications that give tenants their own database (or route them to different servers). Configure it with `UseConnectionStrings` and read connection strings through [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md).

Set at least one delegate. [`ITenantConnectionStringResolver<TKey>.ResolveAsync`](tenantry-core-itenantconnectionstringresolver.md) prefers [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-core-tenantconnectionstringoptions.md) and falls back to [`TenantConnectionStringOptions<TKey>.GetConnectionString`](tenantry-core-tenantconnectionstringoptions.md); the synchronous `Resolve` needs [`TenantConnectionStringOptions<TKey>.GetConnectionString`](tenantry-core-tenantconnectionstringoptions.md). The resolver does not cache.

```csharp
public sealed class TenantConnectionStringOptions<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

## Properties

### `GetConnectionString`

Returns the connection string for a tenant. It runs whenever a connection string is resolved (for example each time a `DbContext` is created), so it should be quick: build the string from the tenant's properties rather than calling another service.

```csharp
public Func<ITenantDescriptor<TKey>, string>? GetConnectionString { get; set; }
```

Value: `Func<ITenantDescriptor<TKey>, string>`

```csharp
options.GetConnectionString = tenant => $"Server=db;Database=app_{tenant.TenantId};Integrated Security=true";
```

### `GetConnectionStringAsync`

Returns the connection string for a tenant asynchronously, for connection strings held elsewhere such as a secrets vault. Only the asynchronous `ResolveAsync` methods can use it.

```csharp
public Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<string>>? GetConnectionStringAsync { get; set; }
```

Value: `Func<ITenantDescriptor<TKey>, CancellationToken, ValueTask<string>>`
