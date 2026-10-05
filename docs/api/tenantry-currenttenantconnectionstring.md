# `CurrentTenantConnectionString<TKey>` class

Namespace: `Tenantry` · Package: `Tenantry.Core` · [API reference](README.md)

Returns the current tenant's connection string, through the registered [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md).

Registered as a singleton by `UseConnectionStrings`, for code that opens its own connections. For EF Core, Tenantry.EfCore's `AddDbContextPerTenantDatabase` connects each context to its tenant's database, pooled or not.

Do not read it in the options callback of `AddDbContextPool`, `AddPooledDbContextFactory` or `AddDbContextFactory`, or of `AddDbContext` with `optionsLifetime: ServiceLifetime.Singleton`: each keeps its options as a singleton (`AddDbContextFactory` by default) and runs that callback once, so every context, for every tenant, would keep the first tenant's connection string. Use `AddDbContextPerTenantDatabase`.

```csharp
public sealed class CurrentTenantConnectionString<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md) for constraints.

## Constructors

### `CurrentTenantConnectionString(ITenantContext<TKey>, ITenantConnectionStringProvider<TKey>)`

Returns the current tenant's connection string, through the registered [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md).

```csharp
public CurrentTenantConnectionString(ITenantContext<TKey> tenantContext, ITenantConnectionStringProvider<TKey> connectionStrings)
```

Parameters:

- `tenantContext` [`ITenantContext<TKey>`](tenantry-itenantcontext.md): Supplies the current tenant.
- `connectionStrings` [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md): Returns a tenant's connection string.

Registered as a singleton by `UseConnectionStrings`, for code that opens its own connections. For EF Core, Tenantry.EfCore's `AddDbContextPerTenantDatabase` connects each context to its tenant's database, pooled or not.

Do not read it in the options callback of `AddDbContextPool`, `AddPooledDbContextFactory` or `AddDbContextFactory`, or of `AddDbContext` with `optionsLifetime: ServiceLifetime.Singleton`: each keeps its options as a singleton (`AddDbContextFactory` by default) and runs that callback once, so every context, for every tenant, would keep the first tenant's connection string. Use `AddDbContextPerTenantDatabase`.

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
