# `ITenantAwareDbContext<TKey>` interface

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

Marks a `DbContext` as tenant-aware, exposing the current tenant identifier for use in EF Core global query filters.

EF Core compiles global query filters once and caches the compiled plan. For the filter to reflect the correct tenant on every query, the expression must close over the `DbContext` itself — EF Core re-evaluates `DbContext` property accesses at query-execution time, whereas external service captures (e.g. a captured `ITenantContext<TKey>` injected into the constructor) are evaluated once at plan-compile time and baked in as constants. This behaviour applies specifically to global query filters; inline `.Where()` clauses do re-read per execution.

Implement this interface on your `DbContext`, expose `CurrentTenantId` as a property that delegates to your injected `ITenantContext<TKey>`, then call `modelBuilder.ApplyTenantFilters<TKey, TContext>(this)` inside `OnModelCreating`.

```csharp
public interface ITenantAwareDbContext<out TKey> where TKey : IEquatable<out TKey>, IParsable<out TKey>
```

## Type parameters

- `TKey`: The tenant identifier type. See [`ITenantEntity<TKey>`](tenantry-itenantentity.md) for constraints.

Derived types: [`MultiTenantDbContext<TKey>`](tenantry-efcore-multitenantdbcontext.md).

## Properties

### `CurrentTenantId`

The identifier of the tenant currently in scope for this context, or `null` if no tenant has been resolved.

```csharp
TKey? CurrentTenantId { get; }
```

Value: `TKey`
