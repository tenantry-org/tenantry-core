# `TenantryTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.Core` · [API reference](README.md)

Tenantry's core features on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): the tenant store and per-tenant connection strings.

```csharp
public static class TenantryTenantBuilderExtensions
```

## Methods

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
