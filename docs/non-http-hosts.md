# Non-HTTP hosts

Worker services, scheduled jobs, CLI tools and desktop apps get the same isolation as a web app, from
`Tenantry.Core`, with no ASP.NET Core dependency. With no request or middleware, you decide when a tenant is current,
usually with `ITenantScopeFactory<TKey>`. EF Core filtering, stamping and checks then work as they do on the web.

## Registration with `AddTenantry`

The same `AddTenantry` as in a web app, from `Tenantry.Core`, without the ASP.NET Core methods:

```csharp
using Microsoft.EntityFrameworkCore;

builder.Services.AddTenantry<Guid>(tenant => tenant
    // Where tenants are listed and looked up by id (e.g. a queue message carries only the tenant id).
    .UseInMemoryStore(tenants));

// The same EF Core isolation as in a web app.
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlite(connectionString)
    .UseTenantry());
```

[Registration](core-concepts.md#registration) lists what it registers. A hosted service reads tenants through
`ITenantLookup<TKey>`, not by injecting the scoped store ([lifetimes](tenant-stores.md#registration-and-lifetimes)).
`ITenantLookup` and `RunInScopeAsync` need a store: without one, creating `ITenantLookup` throws
`InvalidOperationException`, so a hosted service that depends on it fails as the host starts. A host that only
creates scopes for tenants it already has (`CreateScope`), or makes them current with `ITenantContextSetter`, needs
no store.

## Running work as a tenant

`ITenantScopeFactory<TKey>` is a singleton, so hosted services can take it in their constructor. Each
scope it creates is a fresh dependency-injection scope (so a fresh `DbContext`) with the tenant active:

```csharp
using Tenantry;

public sealed class InvoiceWorker(ITenantScopeFactory<Guid> scopes, ITenantLookup<Guid> tenants)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var tenant in await tenants.GetAllTenantsAsync(stoppingToken))
        {
            await using var scope = scopes.CreateScope(tenant);

            // Reads are filtered to this tenant and writes are stamped with it.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ProcessAsync(db, stoppingToken);
        }
        // No tenant is active here: each scope restored the previous (empty) state when it was disposed.
    }
}
```

`GetAllTenantsAsync` lists suspended tenants too, and `CreateScope` does not check them. If your app suspends
tenants, skip them with `if (!await activity.IsActiveAsync(tenant, stoppingToken)) continue;`, where `activity` is an
injected `ITenantActivity<TKey>` (see [Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).

Give each tenant its own scope, and therefore its own `DbContext`, so change-tracker state never bleeds
across tenants. Disposing the scope disposes its services while the tenant is still active, then
restores whichever tenant was current before it, in the code that disposed it. That holds for `using`
and `await using`, in loops and when nested.

### When you only have a tenant id

A queue message or a CLI argument often carries just the id. `RunInScopeAsync` looks the tenant up in the
store and runs your work inside its scope:

```csharp
await scopes.RunInScopeAsync(message.TenantId, async (scope, ct) =>
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Orders.Add(new Order { Description = message.Description });
    await db.SaveChangesAsync(ct);
}, cancellationToken);
```

It throws `TenantNotFoundException`, with the id in its `TenantId` property, if the store has no such tenant
(for example a message for a tenant deleted since it was queued); it derives from `TenantNotResolvedException`.
There is an overload whose work returns a value. With `ValidateTenantActivity`, it throws `TenantInactiveException`
for a suspended tenant without running the work.

There is no `CreateScopeAsync(tenantId)`: a scope opened inside an asynchronous lookup would not be current for the
code that awaited it ([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)). Use `RunInScopeAsync`, or
look the tenant up first and call `CreateScope`:

```csharp
var tenant = await tenants.GetTenantAsync(tenantId, ct) ?? throw new InvalidOperationException("Unknown tenant");
await using var scope = scopes.CreateScope(tenant);
```

### Lower level: `ITenantContextSetter.Use`

`ITenantScopeFactory` is built on `ITenantContextSetter<TKey>.Use(tenant)`, which only changes the ambient
tenant and creates no DI scope. It is useful when you already have the services you need, such as in a
console tool with one long-lived scope:

```csharp
var ambient = sp.GetRequiredService<ITenantContextSetter<Guid>>();

using (ambient.Use(tenant))
{
    db.Orders.Add(new Order { Description = "Created by a tool" });
    await db.SaveChangesAsync();
}
// The previous tenant (or none) is restored here.
```

Like `CreateScope`, call it in the method that does the work, not in an `async` helper that returns the handle.

## Work that runs later

Work queued to run after the scope is disposed must not rely on the current tenant: depending on how it was queued,
it has none, or a stale one whose scope's services are already disposed. Capture the tenant id, and run the work by
id when it happens:

```csharp
queue.Enqueue(tenant.TenantId);   // capture the id, not the ambient scope
// … later, on a different turn:
await scopes.RunInScopeAsync(dequeuedId, (scope, ct) => HandleAsync(scope, ct), ct);
```

For the same reason, do not keep `CurrentTenant` in a singleton's field. The rules for `async` code and threads are
in [the `AsyncLocal` model](core-concepts.md#the-asynclocal-model).

## Runnable sample

[`Tenantry.Samples.EfCoreConsole`](../samples/Tenantry.Samples.EfCoreConsole) uses `Host.CreateApplicationBuilder`,
SQLite and a plain `DbContext` with `UseTenantry()`. It shows stamping, read isolation, nested tenants, a cross-tenant
write rejected, fail-closed reads with no tenant, `IgnoreQueryFilters()` for admin access, and a sweep over every tenant
with `ITenantScopeFactory`:

```bash
dotnet run --project samples/Tenantry.Samples.EfCoreConsole
```
