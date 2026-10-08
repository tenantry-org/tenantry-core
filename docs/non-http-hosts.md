# Non-HTTP hosts

Worker services, scheduled jobs, CLI tools and desktop apps get the same isolation as a web app from `Tenantry.Core`,
with no ASP.NET Core dependency. With no request or middleware, you decide when a tenant is current, usually with
`ITenantScopeFactory<TKey>`, and EF Core filters, stamps and checks as it does on the web.

To start from a generated project, `dotnet new install Tenantry.Templates` and then
`dotnet new tenantry-worker -n Orders.Worker` create a worker service with EF Core. It runs each message as the tenant
it names, with `RunInScopeAsync`. It targets `net10.0`, so building it needs the .NET 10 SDK.

## Registration with `AddTenantry`

Use the same `AddTenantry` as a web app, without the ASP.NET Core methods:

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

`ITenantLookup` and `RunInScopeAsync` need a store. Without one, a hosted service that depends on `ITenantLookup` fails
as the host starts ([`ITenantLookup`](api/tenantry-itenantlookup.md)). A host that only creates scopes for tenants it
already has (`CreateScope`), or makes them current with `ITenantContextSetter`, needs no store.

## Running work as a tenant

`ITenantScopeFactory<TKey>` (`scopes` below) and `ITenantContextSetter<TKey>` are singletons, so hosted services can
take them in their constructor. Choose by what you have:

| Call | When | DI scope | Checks |
|------|------|----------|--------|
| `scopes.RunInScopeAsync(tenantId, work)` | You have an id, such as from a queue message or a command-line argument | Opens one | Looks the tenant up and refuses a missing or inactive one |
| `scopes.CreateScope(tenant)` | You already loaded the tenant: iterating the store, or onboarding one before its store row exists | Opens one | None |
| `tenantContext.MakeCurrent(tenant)` | You already loaded the tenant and a scope already exists: custom middleware, or a framework that opened the scope, such as a message consumer | None | None |

Run work that starts from an id with `RunInScopeAsync`. `CreateScope` and `MakeCurrent` trust the descriptor they are
given, so pass them only a tenant you already hold. A descriptor the store does not hold becomes current like any
other: shared-database queries are filtered by its id and new rows are stamped with it.

`IServiceProvider.CreateScope()` and `CreateAsyncScope()` are .NET's plain DI scopes and set no tenant.

### When you have a tenant id

`RunInScopeAsync` looks the tenant up in the store and runs your work, with the tenant current, inside a new DI scope
that has its own `DbContext`:

```csharp
await scopes.RunInScopeAsync(message.TenantId, async (scope, ct) =>
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Orders.Add(new Order { Description = message.Description });
    await db.SaveChangesAsync(ct);
}, cancellationToken);
```

- A tenant the store does not have throws `TenantNotFoundException`, for example for a message queued before the
  tenant was deleted. It is a `TenantNotResolvedException` with the id in its `TenantId` property.
- With `ValidateTenantActivity`, a suspended tenant throws `TenantInactiveException`, and the work does not run.
- An overload takes work that returns a value.
- `RunInScopeAsync` takes the work as a callback because a scope opened inside an asynchronous lookup would not be
  current for the code that awaited it ([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)).

`RunInScopeAsync` and `CreateScope` open no log scope, so entries the work writes, EF Core's included, carry no
`TenantId`. To add it, open one around the call ([Logs](diagnostics.md#logs)):

```csharp
using (logger.BeginScope(TenantTelemetry.CreateLogScope(message.TenantId)))
{
    await scopes.RunInScopeAsync(message.TenantId, (scope, ct) => HandleAsync(scope, message, ct), cancellationToken);
}
```

To hold the scope yourself, do what `RunInScopeAsync` does: look the tenant up, check it is active, then call
`CreateScope`. `activity` is `ITenantActivity<TKey>`:

```csharp
var tenant = await tenants.GetTenantAsync(tenantId, ct) ?? throw new TenantNotFoundException(tenantId);
await activity.ThrowIfInactiveAsync(tenant, ct);
await using var scope = scopes.CreateScope(tenant);
```

### When you already hold the tenant

`CreateScope` makes a tenant you already have current, with a new DI scope:

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

- `GetAllTenantsAsync` lists suspended tenants too, and `CreateScope` does not check them. If your app suspends tenants,
  skip them with `if (!await activity.IsActiveAsync(tenant, stoppingToken)) continue;`, where `activity` is an injected
  `ITenantActivity<TKey>` ([Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).
- Give each tenant its own scope, and so its own `DbContext`, so no change-tracker state crosses tenants.
- Disposing the scope disposes its services while the tenant is still current. It then restores whichever tenant was
  current before it, in the code that disposed it. This holds with `using` or `await using`, in loops and when nested.

### Lower level: `ITenantContextSetter.MakeCurrent`

`ITenantScopeFactory` is built on `ITenantContextSetter<TKey>.MakeCurrent(tenant)`, which only changes the ambient
tenant. Use it in a scope something else opened, such as in custom middleware, a message consumer whose framework
opened the scope, or a console tool with one long-lived scope:

```csharp
var tenantContext = sp.GetRequiredService<ITenantContextSetter<Guid>>();

using (tenantContext.MakeCurrent(tenant))
{
    db.Orders.Add(new Order { Description = "Created by a tool" });
    await db.SaveChangesAsync();
}
// The previous tenant (or none) is restored here.
```

Like `CreateScope`, call it in the method that does the work, not in an `async` helper that returns the handle
([the `AsyncLocal` model](core-concepts.md#the-asynclocal-model)).

## Work that runs later

Work queued to run after the scope is disposed has no current tenant, or a stale one whose scope's services are
already disposed, depending on how it was queued. Capture the tenant id, and run the work by id when it happens:

```csharp
queue.Enqueue(tenant.TenantId);   // capture the id, not the ambient scope
// … later, on a different turn:
await scopes.RunInScopeAsync(dequeuedId, (scope, ct) => HandleAsync(scope, ct), ct);
```

For the same reason, do not keep `CurrentTenant` in a singleton's field. The rules for `async` code and threads are
in [the `AsyncLocal` model](core-concepts.md#the-asynclocal-model).

## Desktop apps

Await `RunInScopeAsync` on a desktop app's UI thread: it returns to the thread's synchronization context, so blocking on
it there deadlocks. The work you pass starts on the context you called it from, so it can update the UI, and the
scope's services are disposed there too, where they were created.

Tenantry's own reads do not return to the context. A store read, an activity check or a connection string that your
code waits for on the UI thread does not deadlock, though it still blocks the UI while it runs. Your own store and
delegates should use `ConfigureAwait(false)` for the same reason.

## Runnable sample

[`Tenantry.Samples.EfCoreConsole`](../samples/Tenantry.Samples.EfCoreConsole) uses `Host.CreateApplicationBuilder`,
SQLite and a plain `DbContext` with `UseTenantry()`. It shows stamping, read isolation, nested tenants, a cross-tenant
write rejected, fail-closed reads with no tenant, `IgnoreQueryFilters()` for admin access, and a sweep over every tenant
with `ITenantScopeFactory`:

```bash
dotnet run --project samples/Tenantry.Samples.EfCoreConsole
```
