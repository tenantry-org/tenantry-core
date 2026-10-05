# `TenantryEfCoreTenantBuilderExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.EfCore` · [API reference](README.md)

Extension methods for configuring EF Core tenant isolation on [`ITenantBuilder<TKey>`](tenantry-itenantbuilder-1.md).

```csharp
public static class TenantryEfCoreTenantBuilderExtensions
```

## Methods

### `AddDbContextPerTenantDatabase<TContext>(ITenantBuilder, Action<IServiceProvider, DbContextOptionsBuilder>, bool, int)`

Registers `TContext` for a database per tenant: each context is connected to the current tenant's database, through [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md), and uses `UseTenantry()`.

```csharp
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
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

- `InvalidOperationException`: No [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md) is registered yet, or `TContext` is already registered this way. The first context created throws it when the registered provider is scoped or transient.

Use it instead of `AddDbContext` and `AddDbContextPool`: it sets each context's connection string when the context, or a pooled context's lease, is handed out, which EF Core has no hook for. It registers a scoped `TContext` and a singleton `IDbContextFactory<TContext>`. A context that is not pooled gets the other services its constructor needs, and its application service provider, from its scope (the scoped context) or the root provider (the factory's). Creating a context without a current tenant throws [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md), so `dotnet ef` needs an `IDesignTimeDbContextFactory`.

The options get `UseTenantry()` before `configure` runs, so interceptors added there, such as an audit log, see new entities already stamped, and one that changes what a save writes (a soft delete) runs after Tenantry's checks and is not checked.

When the provider cannot read connection strings synchronously ([`ITenantConnectionStringProvider<TKey>.CanGetSynchronously`](tenantry-itenantconnectionstringprovider.md)), a context created synchronously, such as the scoped `TContext`, reads its connection string when it first opens a connection, so only asynchronous EF Core calls work on it.

Before a context opens a connection and before every command, even on an open connection, a guard throws [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) unless the connection was set for the context (and, pooled, its current lease) and for the tenant current now.

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

These are the defaults for every context. A context registered with `UseTenantry(configure)` uses its own instead, so keep the defaults strict and relax them only on a context for maintenance code. The options are ordinary `IOptions<EfCoreIsolationOptions>`, so `services.Configure` also sets them.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseInMemoryStore(tenants)
    .ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Warn));
```
