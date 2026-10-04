# Tenant stores

A tenant store finds a tenant by id (`GetTenantAsync`) or by the identifier a request carries
(`FindByIdentifierAsync`), returning `null` when there is none, and lists every tenant, suspended ones included
(`GetAllTenantsAsync`). Whether work may run for a tenant is decided separately: see
[Suspended and inactive tenants](#suspended-and-inactive-tenants).

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

## Registration and lifetimes

Register one store; a second throws. `UseInMemoryStore` registers a singleton. `UseStore<T>()` and
`UseStore(factory)` register a scoped store, which can use a scoped `DbContext` but must not keep state across calls.
Tenantry reads the store through `ITenantLookup<TKey>`, which resolves it from a new scope for each lookup;
singletons such as hosted services should do the same rather than inject the store. `app.UseTenantry()` fails at
startup without a store. A [non-HTTP host](non-http-hosts.md) that only calls `CreateScope` with descriptors it
already holds needs none, but `ITenantLookup` and `RunInScopeAsync` throw `InvalidOperationException` without one.

## In-memory store

For tests, demos, and simple single-instance deployments where tenants do not change at runtime:

```csharp
tenant.UseInMemoryStore(
[
    new TenantDescriptor<Guid> { TenantId = acmeId,   Name = "Acme" },
    new TenantDescriptor<Guid> { TenantId = globexId, Name = "Globex" },
]);
```

This registers `InMemoryTenantStore<TKey>`, built when you register it. It does not see later
changes to the collection. Two tenants with the same id, or a tenant with an id Tenantry reserves for "no tenant"
(`Guid.Empty`, `0`, an empty string), throw `ArgumentException` at registration.

## Custom store

For tenants in a database, a cache or a configuration service, implement `ITenantStore<TKey>`.

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
// 1. By type, created through dependency injection.
tenant.UseStore<EfCoreTenantStore>();

// 2. By factory, when you need IServiceProvider to construct it.
tenant.UseStore(sp => new EfCoreTenantStore(sp.GetRequiredService<AppDbContext>()));
```

Both are scoped ([Registration and lifetimes](#registration-and-lifetimes)). To avoid a database round trip on
every request, [cache the tenants](#caching).

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

- An HTTP request gets `403 Forbidden` where a tenant is required, and runs without a tenant elsewhere, as for a
  tenant an [access validator](access-control.md#validating-tenant-access) refuses.
- `RunInScopeAsync` throws `TenantInactiveException`, a `TenantNotResolvedException`.
- Tenantry.Pro's background services, schedulers and message integrations skip it.

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

If your tenants are in the same database as your tenant-owned entities, the `Tenant` entity must not implement
`ITenantEntity<TKey>`: with no tenant current, the query filter would hide every row from the store. The
[`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) has a `Tenant` entity, an `EfCoreTenantStore` and
seeded data.

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

When a tenant changes (it is suspended, renamed or deleted, or its slug changes), invalidate it with
`ITenantInvalidator<TKey>`, or it is served as it was until its entry expires: `ValidateTenantActivity` checks
the cached descriptor.

```csharp
app.MapPost("/admin/tenants/{id}/suspend", async (string id, AppDbContext db, ITenantInvalidator<string> tenants, CancellationToken ct) =>
{
    var t = await db.Tenants.SingleOrDefaultAsync(t => t.TenantId == id, ct);
    if (t is null) return Results.NotFound();

    t.IsActive = false;
    await db.SaveChangesAsync(ct);
    await tenants.InvalidateAsync(id, ct);   // by its id and every identifier it was found by
    return Results.NoContent();
});
```

`AddTenantry` always registers `ITenantInvalidator<TKey>`, so this code runs with caching off too, when there is no
cached copy to remove. Each instance of the application has its own cache, so `InvalidateAsync` clears this instance's
copy; other instances serve theirs until it expires. Keep the duration as short as that staleness allows. The cache
reads the time from a registered `TimeProvider`, so tests can control expiry.

### Everything kept for a tenant

`InvalidateAsync` also runs every registered `ITenantInvalidationHandler<TKey>`, with or without `CacheTenants`.
Tenantry.Caching, `IsolateOutputCache()` and Tenantry.Options each register one, so one call clears all of them.
Register your own for data you keep per tenant. When your code also injects the class to read from it, register it
once and point the handler registration at that instance, so both use the same data:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection.Extensions;

builder.Services.AddSingleton<PriceListCache>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantInvalidationHandler<Guid>, PriceListCache>(
    sp => sp.GetRequiredService<PriceListCache>()));

public sealed class PriceListCache : ITenantInvalidationHandler<Guid>
{
    private readonly ConcurrentDictionary<Guid, decimal[]> _prices = new();

    public decimal[] GetOrAdd(Guid tenantId, Func<Guid, decimal[]> load) => _prices.GetOrAdd(tenantId, load);

    public ValueTask InvalidateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _prices.TryRemove(tenantId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
    {
        _prices.Clear();
        return ValueTask.CompletedTask;
    }
}
```

Each handler runs even when another throws; the exception is thrown once they have all run. Invalidating an id
Tenantry reserves for "no tenant" (`Guid.Empty`, `0`, an empty string) throws, since no tenant has it.
