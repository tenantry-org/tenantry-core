# `ITenantConnectionStringResolver<TKey>` interface

Namespace: `Tenantry.Core` · Package: `Tenantry.Core` · [API reference](README.md)

Returns tenants' connection strings, as configured by [`TenantConnectionStringOptions<TKey>`](tenantry-core-tenantconnectionstringoptions.md).

Registered as a singleton by `UseConnectionStrings`. The parameterless overloads use the current tenant (from the request, or from a scope opened with [`ITenantScopeFactory<TKey>`](tenantry-core-itenantscopefactory.md)); the others take the tenant explicitly, for code such as migration runners that visits tenants without making each one current.

With a regular `AddDbContext`, resolve in the options callback, which runs for every new context: `options.UseSqlServer(sp.GetRequiredService<ITenantConnectionStringResolver<Guid>>().Resolve())`. Do not do this with `AddDbContextPool` or `AddPooledDbContextFactory`: their options callback runs once, so every pooled context would keep the first tenant's connection string.

```csharp
public interface ITenantConnectionStringResolver<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md) for constraints.

Derived types: [`TenantConnectionStringResolver<TKey>`](tenantry-core-tenantconnectionstringresolver.md).

## Methods

### `Resolve()`

Returns the current tenant's connection string.

```csharp
string Resolve()
```

Returns: `string`

Exceptions:

- [`TenantNotResolvedException`](tenantry-core-exceptions-tenantnotresolvedexception.md): No tenant is current.
- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-core-tenantconnectionstringoptions.md) is configured (use [`ITenantConnectionStringResolver<TKey>.ResolveAsync`](tenantry-core-itenantconnectionstringresolver.md)), or the delegate returned an empty value.

### `Resolve(ITenantDescriptor<TKey>)`

Returns `tenant`'s connection string.

```csharp
string Resolve(ITenantDescriptor<TKey> tenant)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md): The tenant whose connection string to return.

Returns: `string`

Exceptions:

- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-core-tenantconnectionstringoptions.md) is configured (use [`ITenantConnectionStringResolver<TKey>.ResolveAsync`](tenantry-core-itenantconnectionstringresolver.md)), or the delegate returned an empty value.

### `ResolveAsync(CancellationToken)`

Returns the current tenant's connection string, using the asynchronous delegate if configured.

```csharp
ValueTask<string> ResolveAsync(CancellationToken cancellationToken = default)
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
ValueTask<string> ResolveAsync(ITenantDescriptor<TKey> tenant, CancellationToken cancellationToken = default)
```

Parameters:

- `tenant` [`ITenantDescriptor<TKey>`](tenantry-core-itenantdescriptor.md): The tenant whose connection string to return.
- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<string>`

Exceptions:

- `InvalidOperationException`: The delegate returned an empty value.
