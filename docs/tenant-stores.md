# Tenant stores

A tenant store answers "which tenants exist, and what are their details?" The resolution middleware
calls it with a parsed `TKey` and expects back an `ITenantDescriptor<TKey>` (or `null` if there is no
such tenant). It lists every tenant that exists, suspended ones included; whether a tenant may be served
is decided elsewhere (see [Suspended and inactive tenants](#suspended-and-inactive-tenants)).

```csharp no-compile
public interface ITenantStore<TKey>
{
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken ct = default);
}
```

Exactly one store must be registered. `AddTenantry` validates this at startup and throws a clear
`InvalidOperationException` if no store is registered. (`AddTenantry` does not validate, because a
non-HTTP host may create every scope from a descriptor it already holds and never need a store — see
[Non-HTTP hosts](non-http-hosts.md). `ITenantStoreAccessor` and `ITenantScopeFactory.RunInScopeAsync` do
need one, and say so if it is missing.)

Singletons such as hosted services should read tenants through `ITenantStoreAccessor<TKey>`, which
resolves the store from a fresh scope on each call, rather than injecting a scoped store directly.

## In-memory store

For tests, demos, and simple single-instance deployments where tenants do not change at runtime:

```csharp
tenant.UseInMemoryStore(
[
    new TenantDescriptor<Guid> { TenantId = acmeId,   Name = "Acme" },
    new TenantDescriptor<Guid> { TenantId = globexId, Name = "Globex" },
]);
```

This registers `InMemoryTenantStore<TKey>` as a **singleton**. The collection is indexed by `TenantId`
into a dictionary once, so lookups are O(1). It does not observe changes to the source collection
after registration.

## Custom store

For anything real — tenants in a database, a cache, a config service — implement `ITenantStore<TKey>`.

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public sealed class EfCoreTenantStore(AppDbContext db) : ITenantStore<string>
{
    // Every tenant that exists, active or not. Tenant is your own entity (any ITenantDescriptor<string>).
    public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken ct = default) =>
        await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        await db.Tenants.AsNoTracking().ToListAsync<ITenantDescriptor<string>>(ct);
}
```

Register it one of two ways:

```csharp
// 1. By type — resolved from DI, registered as Scoped.
tenant.UseStore<EfCoreTenantStore>();

// 2. By factory — also Scoped; use when you need IServiceProvider to construct it.
tenant.UseStore(sp => new EfCoreTenantStore(sp.GetRequiredService<AppDbContext>()));
```

> **Lifetimes.** `UseInMemoryStore` registers a **singleton**; `UseStore<T>()` and `UseStore(factory)`
> register **scoped**. Scoped is the right default for stores that depend on a scoped `DbContext`: the
> resolution middleware resolves the store from the request's service scope, so a store sharing the
> request's `DbContext` works correctly. If your store is stateless and cheap, that is fine; if it does
> a database round-trip per request and you want caching, add an `IMemoryCache`/`HybridCache` layer
> inside your implementation.

## Suspended and inactive tenants

Tenantry has no tenant status of its own: a descriptor carries only `TenantId` and `Name`. Keep the
status on your own descriptor type, and keep **every** tenant that exists in the store whatever its
status. Do not hide a suspended tenant by returning `null` or leaving it out of `GetAllTenantsAsync`:
tools that maintain each tenant's database find tenants through the store. Tenantry.Pro's provisioning
and single-tenant migration fail for a tenant the store does not return, and its migration runs,
migration status and health checks cover only the tenants `GetAllTenantsAsync` lists. A tenant hidden
while it is suspended misses every migration and breaks when it is reactivated. For the same reason, keep
a suspended tenant's database available; remove a tenant from the store only once its database has been
archived or dropped.

```csharp
public class Tenant : TenantDescriptor<string>
{
    public bool IsActive { get; set; } = true;   // yours; Tenantry never reads it
}
```

Decide whether a tenant may be served where its work starts:

- **HTTP requests:** add an [access validator](access-control.md#validating-tenant-access). It receives
  the descriptor your store returned and runs before any scope opens; a refused request gets
  `403 Forbidden`.

  ```csharp
  tenant.UseStore<EfCoreTenantStore>();
  tenant.ValidateTenantAccess((http, t) => t is Tenant { IsActive: true });
  ```

- **Background work:** access validators run only in the HTTP middleware, never for
  `ITenantScopeFactory` or background jobs, so check the descriptor yourself:

  ```csharp
  foreach (var t in await tenants.GetAllTenantsAsync(ct))
  {
      if (t is not Tenant { IsActive: true }) continue;   // not active: skip it
      await using var scope = scopes.CreateScope(t);
      // ...
  }
  ```

  With `RunInScopeAsync`, check `scope.Tenant` at the start of the work. Check for the active status, as
  here, rather than for the suspended one, so a descriptor of another type is skipped rather than served.

Once an access validator is configured, an unknown id gets the same response as a refused one (`403` by
default), so a caller cannot tell that a suspended tenant's id exists.

## Bootstrapping with an EF Core-backed store

There is a chicken-and-egg consideration if your tenant registry lives in the same database your
tenanted entities do: the `Tenant` table itself must **not** be a tenanted entity (do not make it
implement `ITenantEntity<TKey>`), or the query filter would prevent the store from reading it before a
tenant is resolved. Keep the tenant registry global. See the
[`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) for a complete example with a `Tenant`
entity, an `EfCoreTenantStore`, and seeded data.

## Caching considerations

The middleware calls `GetTenantAsync` once per request. If that is a database hit you would rather not
take every request, cache inside your store implementation — Tenantry deliberately does not impose a
caching strategy. Remember to invalidate on tenant changes (rename, suspend, delete): an access
validator reads the status from the descriptor your store returns, so a stale cached descriptor keeps a
suspended tenant served.
