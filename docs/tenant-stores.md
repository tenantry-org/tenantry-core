# Tenant stores

Your application already keeps its tenants, usually in a tenants table, so Tenantry reads them through an interface
instead of keeping them itself. A store is two or three queries over that table: find a tenant by id and list every
tenant. When requests name tenants by something other than their id, a third finds one by that name. Tenantry provides
what surrounds those queries: [caching](#caching) with `CacheTenants`, [invalidation](#several-instances) across
instances, the store's [lifetime](#registration-and-lifetimes), and the
[suspension and activity checks](#suspended-and-inactive-tenants). The `EfCoreWeb` sample's store,
[`EfCoreTenantStore.cs`](../samples/Tenantry.Samples.EfCoreWeb/Data/EfCoreTenantStore.cs), is about 40 lines.

```csharp no-compile
public interface ITenantStore<TKey>
{
    ValueTask<ITenantDescriptor<TKey>?> GetTenantAsync(TKey tenantId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ITenantDescriptor<TKey>>> GetAllTenantsAsync(CancellationToken ct = default);

    // Implemented for you: parses the identifier as a TKey and calls GetTenantAsync.
    ValueTask<ITenantDescriptor<TKey>?> FindByIdentifierAsync(string identifier, CancellationToken ct = default);
}
```

Both lookups return `null` when there is none. `GetAllTenantsAsync` lists suspended tenants too: whether work may run
for a tenant is decided [separately](#suspended-and-inactive-tenants). The request middleware finds a request's tenant
with `FindByIdentifierAsync`. Implement it when requests name tenants by something other than their id, such as a
subdomain slug or a custom domain with `Guid` keys
([Identifiers other than the id](tenant-resolution.md#identifiers-other-than-the-id)). A store that wraps another must
forward it ([`FindByIdentifierAsync`](api/tenantry-itenantstore.md)).

## Writing a store

Implement `ITenantStore<TKey>` over wherever your tenants are: a database, a cache or a configuration service.

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

Register it by type or by factory. Both are [scoped](#registration-and-lifetimes):

```csharp
// 1. By type, created through dependency injection.
tenant.UseStore<EfCoreTenantStore>();

// 2. By factory, when you need IServiceProvider to construct it.
tenant.UseStore(sp => new EfCoreTenantStore(sp.GetRequiredService<AppDbContext>()));
```

[Cache the tenants](#caching) to avoid a database round trip on every request.

- When your tenants are in the same database as your tenant-owned entities, the `Tenant` entity must not implement
  `ITenantEntity<TKey>`. With no tenant current, the query filter would hide every row from the store. The
  [`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) has a `Tenant` entity, an `EfCoreTenantStore` and
  seeded data.
- With `string` ids, no two tenants may have ids the database's collation takes for one. A table keyed by the id in
  the same database guarantees it. Tenants read from configuration or another service do not
  ([String tenant ids](efcore-integration.md#string-tenant-ids-and-the-databases-collation)).

## Registration and lifetimes

Register one store: a second throws.

- `UseStore<T>()` and `UseStore(factory)` register a scoped store. It can use a scoped `DbContext`, but must not keep
  state across calls.
- `UseInMemoryStore` registers a singleton.
- Tenantry reads the store through `ITenantLookup<TKey>`, which resolves it from a new scope for each lookup.
  Singletons such as hosted services should do the same, rather than inject the store.

`app.UseTenantry()` fails at startup without a store. A [non-HTTP host](non-http-hosts.md) that only calls
`CreateScope` with descriptors it already holds needs none. Without one, `ITenantLookup` and `RunInScopeAsync` throw
`InvalidOperationException`.

## Suspended and inactive tenants

Return every tenant from the store, suspended ones included, and tell Tenantry which may have work run for them with
`ValidateTenantActivity`. Tenantry has no tenant status of its own: a descriptor carries only `TenantId` and `Name`, so
keep the status on your own descriptor type.

```csharp
public class Tenant : TenantDescriptor<string>
{
    public bool IsActive { get; set; } = true;   // yours; Tenantry reads it only through your check
}
```

```csharp
tenant.UseStore<EfCoreTenantStore>();
tenant.ValidateTenantActivity(t => t is Tenant { IsActive: true });
```

Check for the active status, as here, rather than the suspended one, so a descriptor of another type is refused. Every
check must allow the tenant. Tenantry then refuses an inactive tenant:

- An HTTP request is refused where a tenant is required, with `403 Forbidden` unless you set
  `InactiveTenantStatusCode` ([Status codes](aspnetcore-integration.md#status-codes)). `OnRejected` is told the
  reason is `Inactive` ([Events](aspnetcore-integration.md#events)). Elsewhere the request runs without a tenant,
  even with `app.UseTenantResolution()` ([How the two steps work](authentication-per-tenant.md#how-the-two-steps-work)).
- `RunInScopeAsync` throws `TenantInactiveException`, a `TenantNotResolvedException`.
- Tenantry.Pro's background services, schedulers and message integrations skip it.

A store that hides a suspended tenant breaks other tools. Migration tools, Tenantry.Pro's among them, find tenants
through the store, so a hidden tenant misses migrations and breaks when reactivated. Keep its database until you
remove it from the store.

For a check that needs services, implement `ITenantActivityValidator<TKey>` and add it with
`ValidateTenantActivity<TValidator>()`. It is a singleton, so it cannot take a scoped service such as a `DbContext`:
create a scope inside it instead ([`ValidateTenantActivity<TValidator>()`](api/tenantry-itenantbuilder-1.md)).

```csharp
tenant.UseStore<EfCoreTenantStore>()
    .ValidateTenantActivity<SubscriptionActivityValidator>()
    .CacheTenants();
```

`CreateScope` and `MakeCurrent` do not check activity, so migrations and other work that must reach suspended tenants
can use them ([Running work as a tenant](non-http-hosts.md#running-work-as-a-tenant)). When you loop over tenants for
work of your own, ask `ITenantActivity<TKey>`:

```csharp
foreach (var t in await tenants.GetAllTenantsAsync(ct))
{
    if (!await activity.IsActiveAsync(t, ct)) continue;
    await using var scope = scopes.CreateScope(t);
    // ...
}
```

With [caching](#caching), invalidate a tenant when you suspend it, or it is served until its entry expires.

## In-memory store

For tests, samples and single-instance demos whose tenants do not change at run time:

```csharp
tenant.UseInMemoryStore(
[
    new TenantDescriptor<Guid> { TenantId = acmeId,   Name = "Acme" },
    new TenantDescriptor<Guid> { TenantId = globexId, Name = "Globex" },
]);
```

This registers an [`InMemoryTenantStore<TKey>`](api/tenantry-inmemorytenantstore.md), built when you register it, so it
does not see later changes to the collection. It finds a tenant by its id exactly as written. At registration it
throws `ArgumentException` for ids that would make two tenants one, or name no tenant:

- two tenants with the same id;
- an id Tenantry reserves for "no tenant" (`Guid.Empty`, `0`, an empty string);
- two `string` ids that differ only in case, such as `acme` and `ACME`, which a database whose collation ignores case
  takes for one tenant ([String tenant ids](efcore-integration.md#string-tenant-ids-and-the-databases-collation)).

## Caching

Cache the tenants with `CacheTenants`, and invalidate a tenant when it changes. Without a cache, the middleware asks
the store on every request.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))
    .UseStore<EfCoreTenantStore>()
    .CacheTenants(o => o.Duration = TimeSpan.FromMinutes(1)));   // 5 minutes by default
```

`CacheTenants` keeps each tenant the store finds in memory for the duration, by the id or identifier it was looked up
with. It serves Tenantry's own lookups: the request middleware's and `ITenantLookup<TKey>`'s, which
`ITenantScopeFactory.RunInScopeAsync` and Tenantry.Pro's jobs and messages use. A lookup that finds no tenant is not
cached, so a tenant you add is found at once. `GetAllTenantsAsync` is never cached. Code that injects
`ITenantStore<TKey>` itself reads the store.

When a tenant changes, invalidate it with `ITenantInvalidator<TKey>`, or it is served as it was until its entry
expires. That covers a tenant suspended, renamed or deleted, or whose slug changes. `ValidateTenantActivity` checks the
cached descriptor, so a suspension waits for the invalidation too. When an identifier moves from one tenant to
another, invalidate both.

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

`AddTenantry` always registers `ITenantInvalidator<TKey>`, so this code also runs with caching off. `InvalidateAsync`
clears this instance's cache only: other instances serve their copies until they expire, unless you
[publish the invalidation to them](#several-instances). To test expiry, see
[Cached tenants and time](testing.md#cached-tenants-and-time).

### Everything kept for a tenant

`InvalidateAsync` also runs every registered `ITenantInvalidationHandler<TKey>`, with or without `CacheTenants`.
Tenantry.Caching, `IsolateOutputCache()` and Tenantry.Options each register one, so one call clears all of them.
Register your own for data you keep per tenant. When your code also injects the class to read from it, register it
once and point the handler registration at that instance:

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

Every handler runs even when an earlier one throws. [`InvalidateAsync`](api/tenantry-itenantinvalidator.md) says what
it then throws, and what a cancelled token or a "no tenant" id does.

### Several instances

Each instance of the application keeps its own copies. To clear them everywhere, publish each invalidation through a
channel the instances share (Redis pub/sub, or a message broker):

1. Register the publisher with `BroadcastInvalidations`. `InvalidateAsync` and `InvalidateAllAsync` run it after the
   other handlers, so this instance is cleared first.
2. Tag each message with the instance that sent it, since a subscriber can receive its own.
3. Apply what each instance receives with `InvalidateLocallyAsync` or `InvalidateAllLocallyAsync`. Those run every
   handler except the publishing one, so a received invalidation is not published again.

```csharp
using Microsoft.Extensions.Hosting;

builder.Services.AddSingleton<InstanceId>();
builder.Services.AddHostedService<InvalidationSubscriber>();
builder.Services.AddTenantry<string>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .CacheTenants()
    .BroadcastInvalidations(sp => new InvalidationPublisher(
        sp.GetRequiredService<IInvalidationChannel>(), sp.GetRequiredService<InstanceId>())));

// The channel's interface is yours: wrap Redis pub/sub or your broker's client in it.
public interface IInvalidationChannel
{
    Task PublishAsync(InvalidationMessage message, CancellationToken cancellationToken);
    Task SubscribeAsync(Func<InvalidationMessage, Task> handler, CancellationToken cancellationToken);
}

// TenantId is null to invalidate every tenant.
public sealed record InvalidationMessage(string? TenantId, Guid Sender);

public sealed class InstanceId
{
    public Guid Value { get; } = Guid.NewGuid();
}

public sealed class InvalidationPublisher(IInvalidationChannel channel, InstanceId instance) : ITenantInvalidationHandler<string>
{
    public ValueTask InvalidateAsync(string tenantId, CancellationToken cancellationToken) =>
        new(channel.PublishAsync(new InvalidationMessage(tenantId, instance.Value), cancellationToken));

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken) =>
        new(channel.PublishAsync(new InvalidationMessage(null, instance.Value), cancellationToken));
}

public sealed class InvalidationSubscriber(IInvalidationChannel channel, InstanceId instance, ITenantInvalidator<string> tenants)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        channel.SubscribeAsync(async message =>
        {
            if (message.Sender == instance.Value) return;

            if (message.TenantId is { } tenantId)
                await tenants.InvalidateLocallyAsync(tenantId, stoppingToken);
            else
                await tenants.InvalidateAllLocallyAsync(stoppingToken);
        }, stoppingToken);
}
```

Keep cache durations no longer than you can serve a stale tenant for. An instance that misses a message, because it was
starting or lost its connection, keeps its copies until they expire. When the publisher throws, this instance is
cleared and the others are not
([`BroadcastInvalidations`](api/microsoft-extensions-dependencyinjection-tenantrytenantbuilderextensions.md)). Retry the
call to reach them: clearing this instance again does no harm.
