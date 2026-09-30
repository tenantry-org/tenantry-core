# `TenantryDbContextOptionsBuilderExtensions` class

Namespace: `Microsoft.EntityFrameworkCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Extension methods for wiring Tenantry into `DbContextOptionsBuilder`.

```csharp
public static class TenantryDbContextOptionsBuilderExtensions
```

## Methods

### `UseTenantry(DbContextOptionsBuilder)`

Isolates the context's tenant-owned entities (those implementing [`ITenantEntity<TKey>`](tenantry-itenantentity.md)) by the current tenant: queries return only the current tenant's rows, new entities are saved for the current tenant, saving a change to another tenant's entity throws, and `ExecuteUpdate` cannot set `TenantId`.

```csharp
public static DbContextOptionsBuilder UseTenantry(this DbContextOptionsBuilder optionsBuilder)
```

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder`: The options builder for the application's `DbContext`.

Returns: `DbContextOptionsBuilder`: The same `optionsBuilder` for chaining.

Any `DbContext` works, pooled or not, with no base class or interface. Tenantry adds the tenant query filter after `OnModelCreating`, so your own configuration can come in any order, and it combines the tenant filter with your own filters. On EF Core 10 and later the tenant filter is named [`TenantryQueryFilters.Tenant`](tenantry-efcore-tenantryqueryfilters.md), unless the entity also has an unnamed filter, which it is merged into. It also makes each tenant-owned entity's `TenantId` a concurrency token, so every `UPDATE` and `DELETE` matches only a row stored under the tenant the entity was loaded as.

The current tenant is read from the context's application service provider, which `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory` supply, so Tenantry must be registered there with `AddTenantry` for the tenant key type your entities use. A context without an application service provider (one built by hand without `UseApplicationServiceProvider`) builds its model, for design-time tools, but throws on its first query or save.

Every [`ITenantDbContextOptionsContributor`](tenantry-efcore-itenantdbcontextoptionscontributor.md) registered in the application service provider configures the options here, and every [`ITenantModelContributor`](tenantry-efcore-itenantmodelcontributor.md) the model; without an application service provider, none runs. Calling this again changes nothing.

It installs Tenantry's own EF Core model customizer, so the options must not also replace `IModelCustomizer`, nor use `UseInternalServiceProvider`; creating such a context throws. A compiled model (`dotnet ef dbcontext optimize`) is not supported: EF Core compiles no model with query filters.

```csharp
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());
```

### `UseTenantry<TContext>(DbContextOptionsBuilder<TContext>)`

Isolates the context's tenant-owned entities (those implementing [`ITenantEntity<TKey>`](tenantry-itenantentity.md)) by the current tenant: queries return only the current tenant's rows, new entities are saved for the current tenant, saving a change to another tenant's entity throws, and `ExecuteUpdate` cannot set `TenantId`.

```csharp
public static DbContextOptionsBuilder<TContext> UseTenantry<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder) where TContext : DbContext
```

Type parameters:

- `TContext`: The type of context being configured.

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder<TContext>`: The options builder for the application's `DbContext`.

Returns: `DbContextOptionsBuilder<TContext>`: The same `optionsBuilder` for chaining.

Any `DbContext` works, pooled or not, with no base class or interface. Tenantry adds the tenant query filter after `OnModelCreating`, so your own configuration can come in any order, and it combines the tenant filter with your own filters. On EF Core 10 and later the tenant filter is named [`TenantryQueryFilters.Tenant`](tenantry-efcore-tenantryqueryfilters.md), unless the entity also has an unnamed filter, which it is merged into. It also makes each tenant-owned entity's `TenantId` a concurrency token, so every `UPDATE` and `DELETE` matches only a row stored under the tenant the entity was loaded as.

The current tenant is read from the context's application service provider, which `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory` supply, so Tenantry must be registered there with `AddTenantry` for the tenant key type your entities use. A context without an application service provider (one built by hand without `UseApplicationServiceProvider`) builds its model, for design-time tools, but throws on its first query or save.

Every [`ITenantDbContextOptionsContributor`](tenantry-efcore-itenantdbcontextoptionscontributor.md) registered in the application service provider configures the options here, and every [`ITenantModelContributor`](tenantry-efcore-itenantmodelcontributor.md) the model; without an application service provider, none runs. Calling this again changes nothing.

It installs Tenantry's own EF Core model customizer, so the options must not also replace `IModelCustomizer`, nor use `UseInternalServiceProvider`; creating such a context throws. A compiled model (`dotnet ef dbcontext optimize`) is not supported: EF Core compiles no model with query filters.

```csharp
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());
```
