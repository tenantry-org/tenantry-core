# Non-HTTP hosts

Multi-tenancy is not just an HTTP concern. Worker services that drain a per-tenant queue, scheduled
jobs that run maintenance for every tenant, CLI tools, and desktop apps all need the same isolation.
`Tenantry.Core` provides it with **no dependency on ASP.NET Core**.

The one difference from a web app: there is no request and no middleware, so **you** decide when a
tenant becomes current and when it stops being current. `ITenantScopeFactory<TKey>` does this for background work, and everything
below it (EF Core read filtering and write stamping and validation) behaves exactly as it does on the web.

## Registration with `AddTenantry`

The same `AddTenantry` as in a web app, from `Tenantry.Core`, without the ASP.NET Core methods:

```csharp
using Microsoft.EntityFrameworkCore;

builder.Services.AddTenantry<Guid>(tenant => tenant
    // Where tenants are listed and looked up by id (e.g. a queue message carries only the tenant id).
    .UseInMemoryStore(tenants));

// Same EF Core isolation as the web — strongly recommended in background work.
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlite(connectionString)
    .UseTenantry());
```

`AddTenantry` registers `ITenantContext<TKey>` and `ITenantContextSetter<TKey>` (the same `AsyncLocal`
singleton used by the web layer), plus the `ITenantScopeFactory<TKey>` and `ITenantLookup<TKey>`
singletons described below, and lets you compose stores and isolation.

`ITenantLookup` needs a store: creating it without one throws `InvalidOperationException` naming
`UseStore` and `UseInMemoryStore`, so a hosted service that depends on it fails as the host starts, not on its first
tenant. `ITenantScopeFactory.RunInScopeAsync` looks tenants up too, and throws the same error. A host that only
creates scopes for tenants it already has (`CreateScope`), or makes them current with `ITenantContextSetter`
(below), needs no store.

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

There is deliberately no `CreateScopeAsync(tenantId)`. The tenant lives in an `AsyncLocal`, and an
`async` method's changes to one never reach its caller, so a scope opened inside an asynchronous lookup
would not be active for the code that awaited it. Either use `RunInScopeAsync`, or look the tenant up
first and then call the synchronous `CreateScope`:

```csharp
var tenant = await tenants.GetTenantAsync(tenantId, ct) ?? throw new InvalidOperationException("Unknown tenant");
await using var scope = scopes.CreateScope(tenant);
```

### Reading tenants from a singleton

Use `ITenantLookup<TKey>` rather than injecting `ITenantStore<TKey>` into a hosted service. A
store registered with `UseStore` is scoped and may depend on a `DbContext`; a singleton that captures it
fails scope validation in Development and shares one store instance for the life of the app in
Production. The lookup resolves the store from a fresh scope on each call, whatever its lifetime.

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

Like `CreateScope`, call it directly in the method that does the work, not inside an `async` helper
that returns the handle: the helper's change to the ambient tenant would not reach you.

## Scopes, `async`, and threads

The current tenant is stored in an `AsyncLocal`, so it flows **down** into everything you `await` or
call within the scope, across threads, automatically. It never flows back **up** to a caller. Two
consequences:

- **Fire-and-forget started inside a scope** inherits the tenant at the moment the `Task` is created.
- **Deferred work** (queued to run after the scope disposes) does **not** keep the tenant. Capture the
  tenant id, then run the work by id when it actually happens:

  ```csharp
  queue.Enqueue(tenant.TenantId);   // capture the id, not the ambient scope
  // … later, on a different turn:
  await scopes.RunInScopeAsync(dequeuedId, (scope, ct) => HandleAsync(scope, ct), ct);
  ```

Never cache `CurrentTenant` in a field on a singleton and expect it to be correct later. It is only
valid for the duration of the scope, within the async flow that opened it.

## Runnable sample

[`Tenantry.Samples.EfCoreConsole`](../samples/Tenantry.Samples.EfCoreConsole) is a complete, runnable
demonstration using `Host.CreateApplicationBuilder`, SQLite, and a plain `DbContext` with `UseTenantry()`. It
shows automatic stamping, read isolation, nested tenants, a cross-tenant write rejected,
fail-closed reads with no tenant, `IgnoreQueryFilters()` for admin access, and a sweep over every tenant
with `ITenantScopeFactory`:

```bash
dotnet run --project samples/Tenantry.Samples.EfCoreConsole
```
