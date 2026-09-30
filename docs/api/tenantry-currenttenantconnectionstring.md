# `CurrentTenantConnectionString<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Returns the current tenant's connection string, through the registered [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md).

Registered as a singleton by `UseConnectionStrings`. With a regular `AddDbContext`, read it in the options callback, which runs for every new context: `options.UseSqlServer(sp.GetRequiredService<CurrentTenantConnectionString<Guid>>().Get())`.

Do not do this with `AddDbContextPool` or `AddPooledDbContextFactory`: their options callback runs once, so every pooled context would keep the first tenant's connection string. Use `AddTenantDbContextPool` for a pooled database per tenant.

```csharp
public sealed class CurrentTenantConnectionString<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor.md) for constraints.

## Constructors

### `CurrentTenantConnectionString(ITenantContext<TKey>, ITenantConnectionStringProvider<TKey>)`

Returns the current tenant's connection string, through the registered [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md).

```csharp
public CurrentTenantConnectionString(ITenantContext<TKey> tenantContext, ITenantConnectionStringProvider<TKey> connectionStrings)
```

Parameters:

- `tenantContext` [`ITenantContext<TKey>`](tenantry-itenantcontext.md): Supplies the current tenant.
- `connectionStrings` [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md): Returns a tenant's connection string.

Registered as a singleton by `UseConnectionStrings`. With a regular `AddDbContext`, read it in the options callback, which runs for every new context: `options.UseSqlServer(sp.GetRequiredService<CurrentTenantConnectionString<Guid>>().Get())`.

Do not do this with `AddDbContextPool` or `AddPooledDbContextFactory`: their options callback runs once, so every pooled context would keep the first tenant's connection string. Use `AddTenantDbContextPool` for a pooled database per tenant.

## Methods

### `Get()`

Returns the current tenant's connection string.

```csharp
public string Get()
```

Returns: `string`

Exceptions:

- [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md): No tenant is current.
- `InvalidOperationException`: Only [`TenantConnectionStringOptions<TKey>.GetConnectionStringAsync`](tenantry-tenantconnectionstringoptions.md) is configured (use [`CurrentTenantConnectionString<TKey>.GetAsync`](tenantry-currenttenantconnectionstring.md)), or the delegate returned an empty value.

### `GetAsync(CancellationToken)`

Returns the current tenant's connection string, using the asynchronous delegate if configured.

```csharp
public ValueTask<string> GetAsync(CancellationToken cancellationToken = default)
```

Parameters:

- `cancellationToken` `CancellationToken`: Cancels the lookup.

Returns: `ValueTask<string>`

Exceptions:

- [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md): No tenant is current.
- `InvalidOperationException`: The delegate returned an empty value.
