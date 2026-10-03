# Tenant stores

A tenant store answers "which tenants exist, and what are their details?" It returns an
`ITenantDescriptor<TKey>` for a tenant id, or for an identifier a request carries (or `null` if there is no such
tenant), and lists every tenant that exists, suspended ones included; whether work may run for a tenant is decided
elsewhere (see [Suspended and inactive tenants](#suspended-and-inactive-tenants)).

```csharp no-compile
public interface ITenantStore<TKey>
{
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken ct = default);

    // Implemented for you: parses the identifier as a TKey and calls GetTenantAsync.
    ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(string identifier, CancellationToken ct = default);
}
```

The request middleware finds a request's tenant with `FindByIdentifierAsync`. Implement it when requests name
tenants by something other than their id, such as a subdomain slug or a custom domain with `Guid` keys: see
[Identifiers other than the id](tenant-resolution.md#identifiers-other-than-the-id). A store that wraps another (to
log, say) must forward `FindByIdentifierAsync` too: otherwise it gets the default, which never reaches the inner
store's own mapping.

Exactly one store may be registered: a second `UseStore`/`UseInMemoryStore` throws. A web application must
register one: `app.UseTenantry()` checks at startup and throws a clear `InvalidOperationException` if none is
registered. A non-HTTP host may create every scope from a descriptor it already holds and never need a store —
see [Non-HTTP hosts](non-http-hosts.md). `ITenantLookup` and `ITenantScopeFactory.RunInScopeAsync` do
need one, and say so if it is missing.

Singletons such as hosted services should read tenants through `ITenantLookup<TKey>`, which
resolves the store from a fresh scope on each call, rather than injecting a scoped store directly. The request
middleware reads tenants through it too.

## In-memory store

For tests, demos, and simple single-instance deployments where tenants do not change at runtime:

```csharp
tenant.UseInMemoryStore(
[
    new TenantDescriptor<Guid> { TenantId = acmeId,   Name = "Acme" },
    new TenantDescriptor<Guid> { TenantId = globexId, Name = "Globex" },
]);
```

This registers `InMemoryTenantStore<TKey>` as a **singleton**, built when you register it. It does not see later
changes to the collection. Two tenants with the same id, or a tenant with an id Tenantry reserves for "no tenant"
(`Guid.Empty`, `0`, an empty string), throw `ArgumentException` at registration.

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
> register **scoped**. Scoped is the right default for stores that depend on a scoped `DbContext`:
> `ITenantLookup<TKey>`, which the request middleware and background work use, resolves the store from a
> scope of its own for each lookup. If a lookup is a database round trip you would rather not make on every
> request, [cache the tenants](#caching).

## Suspended and inactive tenants

Tenantry has no tenant status of its own: a descriptor carries only `TenantId` and `Name`. Keep the status on your
own descriptor type, and return every tenant from the store, suspended ones included. Migration tools (Tenantry.Pro's
among them) find tenants through the store, so a hidden tenant misses migrations and breaks when reactivated. Keep its
database until you remove it from the store.

```csharp
public class Tenant : TenantDescriptor<string>
{
    public bool IsActive { get; set; } = true;   // yours; Tenantry reads it only through your check
}
```

Tell Tenantry which tenants may have work run for them with `ValidateTenantActivity`:

```csharp
tenant.UseStore<EfCoreTenantStore>();
tenant.ValidateTenantActivity(t => t is Tenant { IsActive: true });
```

Check for the active status, as here, rather than the suspended one, so a descriptor of another type is refused
rather than served. For a check that needs services, implement `ITenantActivityValidator<TKey>` and register it as a
singleton; every registered check must allow the tenant. Tenantry then refuses an inactive tenant:

- **HTTP requests** get `403 Forbidden` where a tenant is required, and run without a tenant elsewhere, as for a
  tenant an [access validator](access-control.md#validating-tenant-access) refuses.
- **`RunInScopeAsync`** throws `TenantInactiveException`, a `TenantNotResolvedException`.
- **Tenantry.Pro's** background services, schedulers and message integrations skip it.

`CreateScope` does not check, because it takes a tenant you already hold, for work such as migrations that must
reach suspended tenants. When you loop over tenants for work of your own, ask `ITenantActivity<TKey>`:

```csharp
foreach (var t in await tenants.GetAllTenantsAsync(ct))
{
    if (!await activity.IsActiveAsync(t, ct)) continue;
    await using var scope = scopes.CreateScope(t);
    // ...
}
```

With [caching](#caching), invalidate a tenant when you suspend it, or it is served until its entry expires.

## Bootstrapping with an EF Core-backed store

There is a chicken-and-egg consideration if your tenant registry lives in the same database your
tenanted entities do: the `Tenant` table itself must **not** be a tenanted entity (do not make it
implement `ITenantEntity<TKey>`), or the query filter would prevent the store from reading it before a
tenant is resolved. Keep the tenant registry global. See the
[`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) for a complete example with a `Tenant`
entity, an `EfCoreTenantStore`, and seeded data.

## Caching

The middleware looks the request's tenant up on every request. To serve requests without asking the store each
time, cache the tenants:

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .CacheTenants(o => o.Duration = TimeSpan.FromMinutes(1)));   // 5 minutes by default
```

`CacheTenants` keeps each tenant the store finds, in memory, for the duration, by the id or identifier it was
looked up with. It serves Tenantry's own lookups: the request middleware's and `ITenantLookup<TKey>`'s
(which `ITenantScopeFactory.RunInScopeAsync` and Tenantry.Pro's jobs and messages use). A lookup that finds no
tenant is not cached, so a tenant you add is found at once, and `GetAllTenantsAsync` is never cached. Code that
injects `ITenantStore<TKey>` itself reads the store.

When a tenant changes (it is suspended, renamed or deleted, or its slug changes), remove it from the cache, or it
is served as it was until its entry expires: an access validator reads the status from the cached descriptor.

```csharp
app.MapPost("/admin/tenants/{id}/suspend", async (string id, AppDbContext db, ITenantStoreCache<string> cache) =>
{
    var t = await db.Tenants.SingleOrDefaultAsync(t => t.TenantId == id);
    if (t is null) return Results.NotFound();

    t.IsActive = false;
    await db.SaveChangesAsync();
    cache.Invalidate(id);   // by its id and every identifier it was found by
    return Results.NoContent();
});
```

`AddTenantry` always registers `ITenantStoreCache<TKey>`, so this code runs with caching off too, when there is
nothing to remove. Each instance of the application has its own cache, so `Invalidate` clears this instance's copy;
other instances serve theirs until it expires. Keep the duration as short as that staleness allows. The cache reads the time from
a registered `TimeProvider`, so tests can control expiry.

### Everything kept for a tenant

`Invalidate` also runs every registered `ITenantInvalidationHandler<TKey>`, with or without `CacheTenants`, so one
call clears everything kept for a tenant: Tenantry.Caching's cache entries, the responses `IsolateOutputCache()` caches,
and Tenantry.Options' options register a handler, and so can your own code that keeps data per tenant:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection.Extensions;

builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<Guid>, PriceListCache>());

public sealed class PriceListCache : ITenantInvalidationHandler<Guid>
{
    private readonly ConcurrentDictionary<Guid, decimal[]> _prices = new();

    public void Invalidate(Guid tenantId) => _prices.TryRemove(tenantId, out _);

    public void InvalidateAll() => _prices.Clear();
}
```

Each handler runs even when another throws; the exception is thrown once they have all run. Invalidating an id
Tenantry reserves for "no tenant" (`Guid.Empty`, `0`, an empty string) throws, since no tenant has it.
