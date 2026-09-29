# `TenantModelBuilderExtensions` class

Namespace: `Tenantry.EfCore.Extensions` · Package: `Tenantry.EfCore` · [API reference](README.md)

Extension methods for `ModelBuilder` that apply tenant isolation to all entities implementing [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md).

```csharp
public static class TenantModelBuilderExtensions
```

## Methods

### `ApplyTenantFilters<TKey, TContext>(ModelBuilder, TContext)`

Discovers all entity types in the model that implement [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) and applies a global query filter that restricts results to the current tenant. Also marks `TenantId` as a concurrency token, so every `UPDATE` and `DELETE` only matches a row stored under the tenant the entity was loaded or attached with. No index is added: every tenant-filtered query compares `TenantId`, so index it yourself, usually as the leading column of composite indexes that match your queries.

```csharp
[RequiresDynamicCode("Expression tree construction requires dynamic code generation.")]
[RequiresUnreferencedCode("Iterates model entity types and accesses members by name.")]
public static void ApplyTenantFilters<TKey, TContext>(this ModelBuilder modelBuilder, TContext context) where TKey : IEquatable<TKey>, IParsable<TKey> where TContext : DbContext, ITenantAwareDbContext<TKey>
```

Type parameters:

- `TKey`: The tenant identifier type. Must match the key type used in [`ITenantScoped<TKey>`](tenantry-core-itenantscoped.md) and the Tenantry DI registration.
- `TContext`: A `DbContext` subtype that also implements [`ITenantAwareDbContext<TKey>`](tenantry-efcore-itenantawaredbcontext.md).

Parameters:

- `modelBuilder` `ModelBuilder`: The model builder from `OnModelCreating`.
- `context` `TContext`: The calling `DbContext` instance. Pass `this` from inside `OnModelCreating`:  ```csharp modelBuilder.ApplyTenantFilters{Guid, AppDbContext}(this); ```

EF Core caches the compiled query plan but re-evaluates `DbContext` property accesses on every execution. By closing the filter over the `DbContext` (rather than an external `ITenantContext<TKey>` service), the correct tenant ID is always used even though the model is built once and shared across context instances.

Implement [`ITenantAwareDbContext<TKey>`](tenantry-efcore-itenantawaredbcontext.md) on your `DbContext` and expose `CurrentTenantId` as a property that delegates to your injected `ITenantContext<TKey>`. This method is idempotent and combines the tenant filter with any existing query filters.
