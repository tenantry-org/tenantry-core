# `TenantryDbContextPoolServiceCollectionExtensions` class

Namespace: `Microsoft.Extensions.DependencyInjection` · Package: `Tenantry.EfCore` · [API reference](README.md)

DbContext pooling for applications that give each tenant its own database.

```csharp
public static class TenantryDbContextPoolServiceCollectionExtensions
```

## Methods

### `AddTenantDbContextPool<TContext, TKey>(IServiceCollection, Action<IServiceProvider, DbContextOptionsBuilder>, int)`

Pools `TContext` for a database per tenant. Registers a scoped `TContext` and `IDbContextFactory<TContext>`, both leasing from one pool, and connects every lease to the current tenant's database through [`ITenantConnectionStringProvider<TKey>`](tenantry-itenantconnectionstringprovider.md).

```csharp
public static IServiceCollection AddTenantDbContextPool<TContext, TKey>(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> optionsAction, int poolSize = 1024) where TContext : DbContext where TKey : IEquatable<TKey>, IParsable<TKey>
```

Type parameters:

- `TContext`: The context type. It needs a constructor that takes only its options.
- `TKey`: The tenant identifier type.

Parameters:

- `services` `IServiceCollection`: The application's service collection.
- `optionsAction` `Action<IServiceProvider, DbContextOptionsBuilder>`: Configures the context's options, without a connection string: each lease is connected to the current tenant's database.
- `poolSize` `int`: The most contexts the pool keeps for reuse.

Returns: `IServiceCollection`

A pooled context keeps its connection string when it returns to the pool, so resolving the connection string in a regular `AddDbContextPool` callback (which runs once) would send every tenant to the first tenant's database. Here, configure the provider *without* a connection string; each lease gets the current tenant's. Leasing without a current tenant throws `TenantNotResolvedException`.

A guard also checks each pooled context before it opens a connection and before every command it runs, including on a connection that is already open: the connection must have been set for the context's current lease and for the tenant that is current now. Otherwise it throws `TenantIsolationViolationException` rather than use another tenant's database.

The scoped `TContext` reads the connection string synchronously, so it needs [`TenantConnectionStringOptions<TKey>.GetConnectionString`](tenantry-tenantconnectionstringoptions.md). With only an asynchronous delegate, use `IDbContextFactory<TContext>.CreateDbContextAsync`. The context must have a constructor that takes only its options, as [`MultiTenantDbContext<TKey>`](tenantry-efcore-multitenantdbcontext.md) does.

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true")
    .AddEfCoreIsolation());

// No connection string here: each lease is connected to the current tenant's database. builder.Services.AddTenantDbContextPool<AppDbContext, Guid>((sp, options) =>     options.UseSqlServer().AddTenantInterceptors(sp)); ```
