# `ConnectionStringExtensions` class

Namespace: `Tenantry.Core.Extensions` · Package: `Tenantry.Core` · [API reference](README.md)

Registers per-tenant connection strings: [`TenantConnectionStringOptions<TKey>`](tenantry-core-tenantconnectionstringoptions.md) and [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md).

```csharp
public static class ConnectionStringExtensions
```

## Methods

### `AddTenantConnectionStrings<TKey>(IServiceCollection, Action<TenantConnectionStringOptions<TKey>>)`

The `IServiceCollection` form of [`ConnectionStringExtensions.UseConnectionStrings<TKey>`](tenantry-core-extensions-connectionstringextensions.md), for code that has no [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md). Also registers the core Tenantry services.

```csharp
public static IServiceCollection AddTenantConnectionStrings<TKey>(this IServiceCollection services, Action<TenantConnectionStringOptions<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `services` `IServiceCollection`: The application's service collection.
- `configure` `Action<TenantConnectionStringOptions<TKey>>`: Sets the delegates that return a tenant's connection string.

Returns: `IServiceCollection`

Exceptions:

- `InvalidOperationException`: Neither delegate is set after `configure` runs.

Calling it again configures the same options instance, so a later call can replace a delegate.

### `UseConnectionStrings<TKey>(ITenantBuilder<TKey>, Action<TenantConnectionStringOptions<TKey>>)`

Configures how each tenant's connection string is found and registers [`ITenantConnectionStringResolver<TKey>`](tenantry-core-itenantconnectionstringresolver.md) as a singleton.

```csharp
public static ITenantBuilder<TKey> UseConnectionStrings<TKey>(this ITenantBuilder<TKey> builder, Action<TenantConnectionStringOptions<TKey>> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md): The tenant builder.
- `configure` `Action<TenantConnectionStringOptions<TKey>>`: Sets the delegates that return a tenant's connection string.

Returns: [`ITenantBuilder<TKey>`](tenantry-core-itenantbuilder.md)

```csharp
builder.Services.AddTenantry<string>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseStore<AppTenantStore>();
    tenant.UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true");
});

builder.Services.AddDbContext<AppDbContext>((sp, options) =>     options.UseSqlServer(sp.GetRequiredService<ITenantConnectionStringResolver<string>>().Resolve())); ```
