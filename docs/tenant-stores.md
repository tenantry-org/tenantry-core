# Tenant stores

A tenant store answers "which tenants exist, and what are their details?" The resolution middleware
calls it with a parsed `TKey` and expects back an `ITenantDescriptor<TKey>` (or `null` if there is no
such tenant). It is also where you enforce that a tenant is *active*/not suspended.

```csharp
public interface ITenantStore<TKey>
{
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken ct = default);
}
```

Exactly one store must be registered. `AddTenantry` validates this at startup and throws a clear
`InvalidOperationException` if no store is registered. (`AddTenantryCore` does not validate, because a
non-HTTP host may set tenants entirely via `BeginScope` and never need a store — see
[Non-HTTP hosts](non-http-hosts.md).)

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
public sealed class EfCoreTenantStore(AppDbContext db, ILogger<EfCoreTenantStore> logger)
    : ITenantStore<string>
{
    public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

        if (tenant is null) return null;

        // The store is the right place to reject suspended/inactive tenants — return null and the
        // middleware responds 404 (tenant not found), never opening a scope for them.
        if (!tenant.IsActive)
        {
            logger.LogWarning("Tenant {TenantId} is inactive", tenantId);
            return null;
        }

        return tenant; // any ITenantDescriptor<string> implementation
    }

    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        await db.Tenants.AsNoTracking().Where(t => t.IsActive)
            .ToListAsync<ITenantDescriptor<string>>(ct);
}
```

Register it one of three ways:

```csharp
// 1. By type — resolved from DI, registered as Scoped.
tenant.UseStore<EfCoreTenantStore>();

// 2. By factory — also Scoped; use when you need IServiceProvider to construct it.
tenant.UseStore(sp => new EfCoreTenantStore(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ILogger<EfCoreTenantStore>>()));
```

> **Lifetimes.** `UseInMemoryStore` registers a **singleton**; `UseStore<T>()` and `UseStore(factory)`
> register **scoped**. Scoped is the right default for stores that depend on a scoped `DbContext`: the
> resolution middleware resolves the store from the request's service scope, so a store sharing the
> request's `DbContext` works correctly. If your store is stateless and cheap, that is fine; if it does
> a database round-trip per request and you want caching, add an `IMemoryCache`/`HybridCache` layer
> inside your implementation.

## Bootstrapping with an EF Core-backed store

There is a chicken-and-egg consideration if your tenant registry lives in the same database your
tenanted entities do: the `Tenant` table itself must **not** be a tenanted entity (do not make it
implement `ITenantScoped<TKey>`), or the query filter would prevent the store from reading it before a
tenant is resolved. Keep the tenant registry global. See the
[`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) for a complete example with a `Tenant`
entity, an `EfCoreTenantStore`, and seeded data.

## Caching considerations

The middleware calls `GetTenantAsync` once per request. If that is a database hit you would rather not
take every request, cache inside your store implementation — Tenantry deliberately does not impose a
caching strategy. Remember to invalidate on tenant changes (rename, suspend, delete).
