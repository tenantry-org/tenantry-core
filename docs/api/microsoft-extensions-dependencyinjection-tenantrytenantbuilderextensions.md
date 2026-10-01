# `TenantryTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Core` · [API reference](README.md)

Tenantry's core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): the tenant store, its cache and per-tenant connection strings.

```csharp
public static class TenantryTenantBuilderExtensions
```

## Methods

### `CacheTenants<TKey>(ITenantBuilder<TKey>, Action<TenantStoreCacheOptions>?)`

Caches the tenants Tenantry reads from the tenant store, so a request does not ask the store for its tenant each time. [`ITenantStoreCache<TKey>`](tenantry-itenantstorecache.md) then removes a tenant that changes (`AddTenantry` always registers it, so code that invalidates runs with caching off too).

```csharp
public static ITenantBuilder<TKey> CacheTenants<TKey>(this ITenantBuilder<TKey> builder, Action<TenantStoreCacheOptions>? configure = null) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantStoreCacheOptions>`: Sets how long a tenant is cached, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for the default (5 minutes).

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `ArgumentOutOfRangeException`: [`TenantStoreCacheOptions.Duration`](tenantry-tenantstorecacheoptions.md) is not positive.

The cache serves Tenantry's own lookups: `app.UseTenantry()`'s, and [`ITenantStoreAccessor<TKey>`](tenantry-itenantstoreaccessor.md)'s, which [`ITenantScopeFactory<TKey>.RunInScopeAsync`](tenantry-itenantscopefactory.md) and background work use. It keeps each tenant the store finds, by the id or identifier it was looked up with, in memory for [`TenantStoreCacheOptions.Duration`](tenantry-tenantstorecacheoptions.md). A lookup that finds no tenant is not cached, so a tenant added to the store is found at once; [`ITenantStore<TKey>.GetAllTenantsAsync`](tenantry-itenantstore.md) is never cached. Code that injects [`ITenantStore<TKey>`](tenantry-itenantstore.md) reads the store itself.

A tenant that changes (is suspended, say, which an access validator reads) is served as it was until its entry expires: call [`ITenantStoreCache<TKey>.Invalidate`](tenantry-itenantstorecache.md) when you change it. Each instance of the application has its own cache. Calling it again configures the same options. It reads the time from a registered `TimeProvider`, if there is one.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromSubdomain()
    .UseStore<AppTenantStore>()
    .CacheTenants(o => o.Duration = TimeSpan.FromMinutes(1)));
```

### `UseConnectionStrings<TKey>(ITenantBuilder<TKey>, Action<TenantConnectionStringOptions<TKey>>)`

Configures how each tenant's connection string is found, and registers [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) and [`CurrentTenantConnectionString<TKey>`](tenantry-currenttenantconnectionstring.md) as singletons.

```csharp
public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(this ITenantBuilder<TKey> builder, Action<TenantConnectionStringOptions<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<TenantConnectionStringOptions<TKey>>`: Sets the delegates that return a tenant's connection string.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: Neither delegate is set after `configure` runs.

Calling it again configures the same options instance, so a later call can replace a delegate.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true"));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>     options.UseSqlServer(sp.GetRequiredService<CurrentTenantConnectionString<string>>().Get())); ```

### `UseInMemoryStore<TKey>(ITenantBuilder<TKey>, IEnumerable<ITenantDescriptor<TKey>>)`

Registers a pre-populated in-memory tenant store. Suitable for testing and simple single-instance deployments.

```csharp
public static ITenantBuilder<TKey> UseInMemoryStore<TKey>(this ITenantBuilder<TKey> builder, IEnumerable<ITenantDescriptor<TKey>> tenants) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `tenants` `IEnumerable<ITenantDescriptor<TKey>>`: The tenants the store holds. The store does not change after registration.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: A tenant store is already registered.

### `UseStore<TKey>(ITenantBuilder<TKey>, Func<IServiceProvider, ITenantStore<TKey>>)`

Registers a custom [`ITenantStore<TKey>`](tenantry-itenantstore.md) implementation with a factory function.

```csharp
public static ITenantBuilder<TKey> UseStore<TKey>(this ITenantBuilder<TKey> builder, Func<IServiceProvider, ITenantStore<TKey>> factory) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `factory` `Func<IServiceProvider, ITenantStore<TKey>>`: Creates the store from the scope's services.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Exceptions:

- `InvalidOperationException`: A tenant store is already registered.

The store is registered with a **scoped** lifetime and is resolved per operation, so the factory may return an instance that depends on scoped services such as a `DbContext`.
