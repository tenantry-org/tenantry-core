# Non-HTTP hosts

Multi-tenancy is not just an HTTP concern. Worker services that drain a per-tenant queue, scheduled
jobs that run maintenance for every tenant, CLI tools, and desktop apps all need the same isolation.
`Tenantry.Core` provides it with **no dependency on ASP.NET Core**.

The one difference from a web app: there is no request and no middleware, so **you** decide when a
tenant scope begins and ends by calling `ITenantScope<TKey>.BeginScope(...)`. Everything below that —
EF Core read filtering and write stamping/validation — behaves exactly as it does on the web.

## Registration with `AddTenantryCore`

```csharp
using Tenantry.Core;
using Tenantry.Core.Extensions;
using Tenantry.EfCore.Extensions;

builder.Services.AddTenantryCore<Guid>(tenant =>
{
    // A store is optional here — nothing resolves tenants off a request. Register one if you need to
    // look tenants up by id (e.g. a queue message carries only the tenant id).
    tenant.UseInMemoryStore(tenants);

    // Same EF Core isolation as the web — strongly recommended in background work.
    tenant.AddEfCoreIsolation(options => options.StrictIsolation = true);
});

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlite(connectionString)
           .AddTenantInterceptors(sp));
```

`AddTenantryCore` registers `ITenantContext<TKey>` and `ITenantScope<TKey>` (the same `AsyncLocal`
singleton used by the web layer) and lets you compose stores and isolation. Unlike `AddTenantry`, it
does **not** add startup validation for resolvers/stores, because a non-HTTP host may legitimately have
neither.

## Opening a scope around your work

```csharp
var scope = sp.GetRequiredService<ITenantScope<Guid>>();
var tenant = new TenantDescriptor<Guid> { TenantId = tenantId, Name = "Acme" };

using (scope.BeginScope(tenant))
{
    // Inside here, ITenantContext.CurrentTenant == tenant.
    // EF Core reads are filtered to this tenant; writes are stamped with it.
    db.Orders.Add(new Order { Description = "Created by a worker" });
    await db.SaveChangesAsync();
}
// Previous tenant (or "none") is restored here.
```

If your trigger carries only a tenant id (a message, a CLI argument), look the full descriptor up via
the store first:

```csharp
var store = sp.GetRequiredService<ITenantStore<Guid>>();
var tenant = await store.GetTenantAsync(tenantId, ct)
             ?? throw new InvalidOperationException($"Unknown tenant {tenantId}");

using (scope.BeginScope(tenant)) { /* … */ }
```

## Processing every tenant

A common batch pattern — iterate all tenants, doing each in its own scope:

```csharp
var store = sp.GetRequiredService<ITenantStore<Guid>>();
var scope = sp.GetRequiredService<ITenantScope<Guid>>();

foreach (var tenant in await store.GetAllTenantsAsync(ct))
{
    using (scope.BeginScope(tenant))
    using (var work = sp.CreateScope())              // fresh DI scope → fresh DbContext per tenant
    {
        var db = work.ServiceProvider.GetRequiredService<AppDbContext>();
        await ProcessAsync(db, ct);
    }
}
```

Give each tenant its own DI scope (and therefore its own `DbContext`) so change-tracker state never
bleeds across tenants.

## Scopes, `async`, and threads

The current tenant is stored in an `AsyncLocal`, so it flows **down** into everything you `await` or
call within the `using` block, across threads, automatically. Two consequences:

- **Fire-and-forget started inside a scope** inherits the tenant at the moment the `Task` is created.
- **Deferred work** (queued to run after the scope disposes) does **not** keep the tenant. Capture the
  tenant id, then open a fresh scope when that work actually runs:

  ```csharp
  queue.Enqueue(tenant.TenantId);   // capture the id, not the ambient scope
  // … later, on a different turn:
  using (scope.BeginScope(await store.GetTenantAsync(dequeuedId, ct))) { /* … */ }
  ```

Never cache `CurrentTenant` in a field on a singleton and expect it to be correct later — it is only
valid for the duration of the scope, within the async flow that opened it.

## Runnable sample

[`Tenantry.Samples.EfCoreConsole`](../samples/Tenantry.Samples.EfCoreConsole) is a complete, runnable
demonstration using `Host.CreateApplicationBuilder`, SQLite, and `MultiTenantDbContext<Guid>`. It
shows automatic stamping, read isolation, nested scopes, a strict-mode cross-tenant rejection,
fail-closed reads with no scope, and `IgnoreQueryFilters()` for admin access:

```bash
dotnet run --project samples/Tenantry.Samples.EfCoreConsole
```
