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

`ITenantScopeFactory<TKey>` (`scopes` below) and `ITenantContextSetter<TKey>` are singletons, so hosted services can
take them in their constructor. Choose by what you have:

| Call | When | DI scope | Checks |
|------|------|----------|--------|
| `scopes.RunInScopeAsync(tenantId, work)` | You have an id, such as from a queue message or a command-line argument | Opens one | Looks the tenant up and refuses a missing or inactive one |
| `scopes.CreateScope(tenant)` | You already loaded the tenant: iterating the store, or onboarding one before its store row exists | Opens one | None |
| `tenantContext.Use(tenant)` | You already loaded the tenant and a scope already exists: custom middleware, or a framework that opened the scope, such as a message consumer | None | None |

`CreateScope` and `Use` trust the descriptor they are given. They do not look it up in the store or check whether
it is active, so a descriptor the store does not hold becomes current like any other: shared-database queries are
filtered by its id and new rows are stamped with it. Pass them only a tenant you already hold, and run work that
starts from an id with `RunInScopeAsync`. It takes the work as a callback because the tenant is held in an
`AsyncLocal`: a scope opened inside an asynchronous lookup would not be current for the code that awaited it
([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)).

`IServiceProvider.CreateScope()` and `CreateAsyncScope()` are .NET's plain DI scopes and set no tenant.

### When you have a tenant id

A queue message or a CLI argument often carries just the id. `RunInScopeAsync` looks the tenant up in the store and
runs your work inside a fresh DI scope (so a fresh `DbContext`) with the tenant current:

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

To hold the scope yourself, look the tenant up first and call `CreateScope`, checking `ITenantActivity<TKey>` too if
your app suspends tenants:

```csharp
var tenant = await tenants.GetTenantAsync(tenantId, ct) ?? throw new InvalidOperationException("Unknown tenant");
await using var scope = scopes.CreateScope(tenant);
```

### When you already hold the tenant

`CreateScope` makes a tenant you already have current, with a fresh DI scope:

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

### Lower level: `ITenantContextSetter.Use`

`ITenantScopeFactory` is built on `ITenantContextSetter<TKey>.Use(tenant)`, which only changes the ambient
tenant and creates no DI scope. It is for code that runs in a scope something else opened, such as custom middleware,
a message consumer whose framework opened the scope, or a console tool with one long-lived scope. Like `CreateScope`,
it trusts the descriptor:

```csharp
var tenantContext = sp.GetRequiredService<ITenantContextSetter<Guid>>();

using (tenantContext.Use(tenant))
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
