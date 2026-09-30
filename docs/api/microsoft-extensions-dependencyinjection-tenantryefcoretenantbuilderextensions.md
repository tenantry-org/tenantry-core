# `TenantryEfCoreTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.EfCore` · [API reference](README.md)

Extension methods for configuring EF Core tenant isolation on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md).

```csharp
public static class TenantryEfCoreTenantBuilderExtensions
```

## Methods

### `AddDbContextPerTenantDatabase<TContext>(ITenantBuilder, Action<IServiceProvider, DbContextOptionsBuilder>, bool, int)`

Registers `TContext` for a database per tenant: each context is connected to the current tenant's database, through [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md), and uses `UseTenantry()`. Registers a scoped `TContext` and a singleton `IDbContextFactory<TContext>`.

```csharp
public static ITenantBuilder AddDbContextPerTenantDatabase<TContext>(this ITenantBuilder builder, Action<IServiceProvider, DbContextOptionsBuilder> configure, bool pooled = false, int poolSize = 1024) where TContext : DbContext
```

Type parameters:

- `TContext`: The context type.

Parameters:

- `builder` [`ITenantBuilder`](tenantry-itenantbuilder.md): The tenant builder, after `UseConnectionStrings` (or another registration of [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md)).
- `configure` `Action<IServiceProvider, DbContextOptionsBuilder>`: Configures the context's options, *without* a connection string: for example `(sp, options) => options.UseSqlServer()`.
- `pooled` `bool`: Whether to reuse context instances from a pool, as `AddDbContextPool` does. A pooled context needs a constructor that takes only its options.
- `poolSize` `int`: The most contexts the pool keeps for reuse, when `pooled`.

Returns: [`ITenantBuilder`](tenantry-itenantbuilder.md): The same builder, without its key type: call methods that need it (such as `UseConnectionStrings`) first.

Exceptions:

- `InvalidOperationException`: No [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) is registered yet, or `TContext` is already registered this way.

A context that is not pooled is created with its options and any other services its constructor needs, and has them as its application service provider, as with `AddDbContext`: the scoped `TContext` from its scope, and one from the factory from the root provider, as EF Core's `AddDbContextFactory` does. Creating a context without a current tenant throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), so `dotnet ef` needs an `IDesignTimeDbContextFactory` for the context.

The options get `UseTenantry()` before `configure` runs, so interceptors added there (an audit log, say) see new entities already stamped with their tenant.

The scoped `TContext` reads the connection string synchronously, so it needs [`TenantConnectionStringOptions<TKey>.GetConnectionString`](tenantry-tenantconnectionstringoptions.md). With only an asynchronous delegate, use `IDbContextFactory<TContext>.CreateDbContextAsync`.

A guard checks each context before it opens a connection and before every command it runs, including on a connection that is already open: the connection must have been set for the context (and, pooled, for its current lease) and for the tenant that is current now. Otherwise it throws [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) rather than use another tenant's database.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true")
    .AddDbContextPerTenantDatabase<AppDbContext>((sp, options) => options.UseSqlServer(), pooled: true));
```

### `ConfigureEfCoreIsolation<TKey>(ITenantBuilder<TKey>, Action<EfCoreIsolationOptions>)`

Sets the EF Core isolation options, such as what happens to a write without a tenant. Optional: without it, the defaults apply, which are the strictest.

```csharp
public static ITenantBuilder<TKey> ConfigureEfCoreIsolation<TKey>(this ITenantBuilder<TKey> builder, Action<EfCoreIsolationOptions> configure) where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type.

Parameters:

- `builder` [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The tenant builder.
- `configure` `Action<EfCoreIsolationOptions>`: Sets the options.

Returns: [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md): The same `builder` for chaining.

Calling it again configures the same options instance.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseInMemoryStore(tenants)
    .ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));
```
