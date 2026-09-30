# `MultiTenantDbContext<TKey>` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Optional base `DbContext` that automatically applies tenant query filters in `OnModelCreating`.

This class is a convenience for greenfield projects.

To use: derive from [`MultiTenantDbContext<TKey>`](tenantry-efcore-multitenantdbcontext.md), give your context a constructor that takes only its `DbContextOptions<TContext>`, and call `base.OnModelCreating(modelBuilder)` at the end of your override, after your own configuration. The current tenant comes from Tenantry's ambient [`ITenantContext<TKey>`](tenantry-itenantcontext.md), resolved from the application service provider, so the same instance serves whichever tenant is active when it runs a query or saves.

**DbContext pooling is supported.** Register with `AddDbContextPool` or `AddPooledDbContextFactory` and call `options.AddTenantInterceptors(sp)` in the options callback: EF Core does not let [`MultiTenantDbContext<TKey>.OnConfiguring`](tenantry-efcore-multitenantdbcontext.md) change the options of a pooled context, so the self-wiring described below cannot run there, and a pooled context without the interceptors fails on first use instead of saving without isolation.

**Isolation is self-wiring for non-pooled contexts.** When you call `AddEfCoreIsolation()` and register this context through `AddDbContext` (which supplies an application service provider), the tenant interceptors are attached automatically in [`MultiTenantDbContext<TKey>.OnConfiguring`](tenantry-efcore-multitenantdbcontext.md) — you do *not* also need `options.AddTenantInterceptors(sp)`. This prevents the silent-isolation-loss failure mode of forgetting that wiring step. A **raw `DbContext`** that does not derive from this base class must still call `options.AddTenantInterceptors(sp)` in its registration callback.

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : MultiTenantDbContext<Guid>(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)     {         // ... your entity configuration         base.OnModelCreating(modelBuilder); // last: applies tenant filters     } }

// Either builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString)); // or, pooled builder.Services.AddDbContextPool<AppDbContext>((sp, options) =>     options.UseSqlServer(connectionString).AddTenantInterceptors(sp)); ```

```csharp
[RequiresUnreferencedCode("EF Core is not fully compatible with trimming.")]
[RequiresDynamicCode("EF Core is not fully compatible with NativeAOT.")]
public abstract class MultiTenantDbContext<TKey> : DbContext, IInfrastructure<IServiceProvider>, IDbContextDependencies, IDbSetCache, IDbContextPoolable, IResettableService, IDisposable, IAsyncDisposable, ITenantAwareDbContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantEntity<TKey>`](tenantry-itenantentity.md) for constraints.

Inherits `DbContext`.

Implements `IInfrastructure<IServiceProvider>`, `IDbContextDependencies`, `IDbSetCache`, `IDbContextPoolable`, `IResettableService`, `IDisposable`, `IAsyncDisposable`, [`ITenantAwareDbContext<TKey>`](tenantry-efcore-itenantawaredbcontext.md).

## Constructors

### `MultiTenantDbContext(DbContextOptions)`

Initialises a new instance that resolves the ambient [`ITenantContext<TKey>`](tenantry-itenantcontext.md) from the application service provider on first use. Use this constructor for pooled contexts.

```csharp
protected MultiTenantDbContext(DbContextOptions options)
```

Parameters:

- `options` `DbContextOptions`: The options for this context.

### `MultiTenantDbContext(DbContextOptions, ITenantContext<TKey>)`

Initialises a new instance that uses the given [`ITenantContext<TKey>`](tenantry-itenantcontext.md), for contexts created outside dependency injection.

```csharp
protected MultiTenantDbContext(DbContextOptions options, ITenantContext<TKey> tenantContext)
```

Parameters:

- `options` `DbContextOptions`: The options for this context.
- `tenantContext` [`ITenantContext<TKey>`](tenantry-itenantcontext.md): Supplies the current tenant.

## Properties

### `CurrentTenantId`

The identifier of the tenant currently in scope for this context, or `null` if no tenant has been resolved.

```csharp
public TKey? CurrentTenantId { get; }
```

Value: `TKey`

Exceptions:

- `InvalidOperationException`: No [`ITenantContext<TKey>`](tenantry-itenantcontext.md) was passed to the constructor and none is registered in the application service provider.

Reads the ambient tenant each time it is accessed. EF Core re-evaluates this property on every query execution because it accesses a `DbContext` property, so the filter always reflects the tenant active at that moment, including when a pooled instance is reused for another tenant.

## Methods

### `OnConfiguring(DbContextOptionsBuilder)`

Override this method to configure the database (and other options) to be used for this context. This method is called for each instance of the context that is created. The base implementation does nothing.

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
```

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder`: A builder used to create or modify options for this context. Databases (and other extensions) typically define extension methods on this object that allow you to configure the context.

In situations where an instance of `DbContextOptions` may or may not have been passed     to the constructor, you can use `IsConfigured` to determine if     the options have already been set, and skip some or all of the logic in     `OnConfiguring(DbContextOptionsBuilder)`.

See [DbContext lifetime, configuration, and initialization](https://aka.ms/efcore-docs-dbcontext)     for more information and examples.

### `OnModelCreating(ModelBuilder)`

Override this method to further configure the model that was discovered by convention from the entity types exposed in `DbSet<TEntity>` properties on your derived context. The resulting model may be cached and re-used for subsequent instances of your derived context.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
```

Parameters:

- `modelBuilder` `ModelBuilder`: The builder being used to construct the model for this context. Databases (and other extensions) typically define extension methods on this object that allow you to configure aspects of the model that are specific to a given database.

If a model is explicitly set on the options for this context (via `UseModel(IModel)`)     then this method will not be run. However, it will still run when creating a compiled model.

See [Modeling entity types and relationships](https://aka.ms/efcore-docs-modeling) for more information and     examples.
