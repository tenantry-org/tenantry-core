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
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
public static DbContextOptionsBuilder UseTenantry(this DbContextOptionsBuilder optionsBuilder)
```

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder`: The options builder for the application's `DbContext`.

Returns: `DbContextOptionsBuilder`: The same `optionsBuilder` for chaining.

Any `DbContext` works, pooled or not, with no base class or interface. Tenantry adds the tenant query filter after `OnModelCreating`, so your own configuration can come in any order, and it combines the tenant filter with your own filters. On EF Core 10 and later the tenant filter is named [`TenantryQueryFilters.Tenant`](tenantry-efcore-tenantryqueryfilters.md), and an unnamed filter of your own beside it is named [`TenantryQueryFilters.Application`](tenantry-efcore-tenantryqueryfilters.md); on EF Core 8 and 9 it is merged into your filter. It also makes each tenant-owned entity's `TenantId` a concurrency token, so every `UPDATE` and `DELETE` matches only a row stored under the tenant the entity was loaded as.

The current tenant is read from the context's application service provider, which `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory` supply, so Tenantry must be registered there with `AddTenantry` for the tenant key type your entities use. A context without an application service provider (one built by hand without `UseApplicationServiceProvider`) builds its model, for design-time tools, but throws on its first query or save.

Every [`ITenantDbContextOptionsContributor`](tenantry-efcore-itenantdbcontextoptionscontributor.md) registered in the application service provider configures the options here, and every [`ITenantModelContributor`](tenantry-efcore-itenantmodelcontributor.md) the model; without an application service provider, none runs. The application's [`EfCoreIsolationOptions`](tenantry-efcore-efcoreisolationoptions.md) are read here too, so set the application service provider before calling it: otherwise a context whose application sets [`EfCoreIsolationOptions.OnUnclassifiedEntityType`](tenantry-efcore-efcoreisolationoptions.md) to anything but `Reject` throws `InvalidOperationException` on its first query or save. Calling this again changes nothing.

It installs Tenantry's own EF Core model customizer, so the options must not also replace `IModelCustomizer`, nor use `UseInternalServiceProvider`; creating such a context throws. A compiled model (`dotnet ef dbcontext optimize`) is not supported: EF Core compiles no model with query filters.

```csharp
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());
```

### `UseTenantry(DbContextOptionsBuilder, Action<EfCoreIsolationOptions>)`

Isolates the context's tenant-owned entities as [`TenantryDbContextOptionsBuilderExtensions.UseTenantry`](microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md) does, with isolation options of the context's own in place of the application's (`ConfigureEfCoreIsolation`).

```csharp
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
public static DbContextOptionsBuilder UseTenantry(this DbContextOptionsBuilder optionsBuilder, Action<EfCoreIsolationOptions> configure)
```

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder`: The options builder for the application's `DbContext`.
- `configure` `Action<EfCoreIsolationOptions>`: Sets the context's options, starting from the application's. Use it to relax a policy on a context kept for maintenance code, so the rest of the application keeps the strict defaults.

Returns: `DbContextOptionsBuilder`: The same `optionsBuilder` for chaining.

Calling it again sets the options again, starting from the application's.

```csharp
builder.Services.AddDbContext<MaintenanceDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Allow));
```

### `UseTenantry<TContext>(DbContextOptionsBuilder<TContext>)`

Isolates the context's tenant-owned entities (those implementing [`ITenantEntity<TKey>`](tenantry-itenantentity.md)) by the current tenant: queries return only the current tenant's rows, new entities are saved for the current tenant, saving a change to another tenant's entity throws, and `ExecuteUpdate` cannot set `TenantId`.

```csharp
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
public static DbContextOptionsBuilder<TContext> UseTenantry<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder) where TContext : DbContext
```

Type parameters:

- `TContext`: The type of context being configured.

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder<TContext>`: The options builder for the application's `DbContext`.

Returns: `DbContextOptionsBuilder<TContext>`: The same `optionsBuilder` for chaining.

Any `DbContext` works, pooled or not, with no base class or interface. Tenantry adds the tenant query filter after `OnModelCreating`, so your own configuration can come in any order, and it combines the tenant filter with your own filters. On EF Core 10 and later the tenant filter is named [`TenantryQueryFilters.Tenant`](tenantry-efcore-tenantryqueryfilters.md), and an unnamed filter of your own beside it is named [`TenantryQueryFilters.Application`](tenantry-efcore-tenantryqueryfilters.md); on EF Core 8 and 9 it is merged into your filter. It also makes each tenant-owned entity's `TenantId` a concurrency token, so every `UPDATE` and `DELETE` matches only a row stored under the tenant the entity was loaded as.

The current tenant is read from the context's application service provider, which `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory` supply, so Tenantry must be registered there with `AddTenantry` for the tenant key type your entities use. A context without an application service provider (one built by hand without `UseApplicationServiceProvider`) builds its model, for design-time tools, but throws on its first query or save.

Every [`ITenantDbContextOptionsContributor`](tenantry-efcore-itenantdbcontextoptionscontributor.md) registered in the application service provider configures the options here, and every [`ITenantModelContributor`](tenantry-efcore-itenantmodelcontributor.md) the model; without an application service provider, none runs. The application's [`EfCoreIsolationOptions`](tenantry-efcore-efcoreisolationoptions.md) are read here too, so set the application service provider before calling it: otherwise a context whose application sets [`EfCoreIsolationOptions.OnUnclassifiedEntityType`](tenantry-efcore-efcoreisolationoptions.md) to anything but `Reject` throws `InvalidOperationException` on its first query or save. Calling this again changes nothing.

It installs Tenantry's own EF Core model customizer, so the options must not also replace `IModelCustomizer`, nor use `UseInternalServiceProvider`; creating such a context throws. A compiled model (`dotnet ef dbcontext optimize`) is not supported: EF Core compiles no model with query filters.

```csharp
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());
```

### `UseTenantry<TContext>(DbContextOptionsBuilder<TContext>, Action<EfCoreIsolationOptions>)`

Isolates the context's tenant-owned entities as [`TenantryDbContextOptionsBuilderExtensions.UseTenantry`](microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md) does, with isolation options of the context's own in place of the application's (`ConfigureEfCoreIsolation`).

```csharp
[RequiresUnreferencedCode("EF Core and Tenantry's query filters read entity types through reflection, which trimming can break. See https://aka.ms/efcore-docs-trimming.")]
[RequiresDynamicCode("EF Core and Tenantry's query filters build code for entity types at run time, which Native AOT does not support.")]
public static DbContextOptionsBuilder<TContext> UseTenantry<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder, Action<EfCoreIsolationOptions> configure) where TContext : DbContext
```

Type parameters:

- `TContext`: The type of context being configured.

Parameters:

- `optionsBuilder` `DbContextOptionsBuilder<TContext>`: The options builder for the application's `DbContext`.
- `configure` `Action<EfCoreIsolationOptions>`: Sets the context's options, starting from the application's. Use it to relax a policy on a context kept for maintenance code, so the rest of the application keeps the strict defaults.

Returns: `DbContextOptionsBuilder<TContext>`: The same `optionsBuilder` for chaining.

Calling it again sets the options again, starting from the application's.

```csharp
builder.Services.AddDbContext<MaintenanceDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Allow));
```
