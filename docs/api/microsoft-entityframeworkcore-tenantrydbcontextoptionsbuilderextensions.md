# `TenantryDbContextOptionsBuilderExtensions` class

Namespace: `Microsoft.EntityFrameworkCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Extension methods for wiring Tenantry into `DbContextOptionsBuilder`.

```csharp
public static class TenantryDbContextOptionsBuilderExtensions
```

## Methods

### `AddTenantInterceptors(DbContextOptionsBuilder, IServiceProvider)`

Adds Tenantry interceptors to the `DbContextOptionsBuilder`. Requires `AddEfCoreIsolation()` inside `AddTenantry`.

```csharp
public static DbContextOptionsBuilder AddTenantInterceptors(this DbContextOptionsBuilder optionsBuilder, IServiceProvider serviceProvider)
```

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder`: The options builder for the application's `DbContext`.
- `serviceProvider` `IServiceProvider`: The `IServiceProvider` from the `AddDbContext` factory callback, used to resolve the interceptor singleton.

Returns: `DbContextOptionsBuilder`: The same `optionsBuilder` for chaining.

Exceptions:

- `InvalidOperationException`: EF Core isolation is not registered (`AddEfCoreIsolation`).

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddTenantInterceptors(sp));
```
