# Core concepts

Tenantry is built on a few types in the `Tenantry` namespace (package `Tenantry.Core`).

## The tenant key (`TKey`)

`TKey` is the type of your tenant ids, and Tenantry's tenant types are generic over it. `Guid`, `int`, `long`,
`string` and most numeric types work. The constraint is:

```csharp no-compile
where TKey : IEquatable<TKey>, IParsable<TKey>
```

- `IEquatable<TKey>` lets EF Core translate `tenantId == currentTenantId` into SQL for the query filter.
- `IParsable<TKey>` lets Tenantry turn text, such as the identifier a request carries in a header, route or
  claim, into a `TKey`, with the invariant culture.

An application has one key type, used by its entities, store and registration. Calling `AddTenantry` with a second
one throws. An entity that implements `ITenantEntity<string>` in a `Guid` application fails its model's first query or
save, instead of going unisolated.

The key type's default value means "no tenant": `Guid.Empty`, `0`, and for `string` keys `null` or an empty string. No
tenant may have it. Making such a tenant current throws `ArgumentException`, and an identifier that parses to it names
no tenant. For code of your own that carries tenant ids as text, [`TenantIds`](api/tenantry-tenantids.md) formats,
parses and checks them as Tenantry does.

## `ITenantDescriptor<TKey>`

A descriptor is what Tenantry knows about a tenant:

```csharp no-compile
public interface ITenantDescriptor<out TKey> : ITenantDescriptor
{
    TKey TenantId { get; }   // used for data isolation
    string Name { get; }     // human-readable display name (declared on ITenantDescriptor)
}
```

`TenantDescriptor<TKey>` is the default implementation:

```csharp
using Tenantry;

new TenantDescriptor<Guid> { TenantId = id, Name = "Acme" };
```

### Your own tenant type

Implement `ITenantDescriptor<TKey>` on your own type to carry what your application knows about a tenant (its plan,
region, connection string, feature flags), and return it from your [tenant store](tenant-stores.md). Tenantry reads
only `TenantId` and `Name`. It has no tenant status of its own: keep yours on your tenant type, and give Tenantry a
check for it with `ValidateTenantActivity`
([Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).

```csharp no-compile
public class AppTenant : ITenantDescriptor<Guid>
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Plan { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public bool IsSuspended { get; set; }
}
```

Every delegate and service that receives a tenant receives it as `ITenantDescriptor<TKey>`. Read your own
properties with `As<TTenant>()`, and the current tenant with `GetCurrentTenant<TTenant>()`:

```csharp
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<EfCoreTenantStore>()
    .UseConnectionStrings(o => o.GetConnectionString = t => t.As<AppTenant>().ConnectionString)
    .ValidateTenantActivity(t => !t.As<AppTenant>().IsSuspended));

app.MapGet("/plan", (ITenantContext<Guid> tenants) => tenants.GetCurrentTenant<AppTenant>()?.Plan);
```

Both throw `InvalidOperationException` if the store returned a tenant of another type
([`As<TTenant>()`](api/tenantry-tenantdescriptorextensions.md)).

## `ITenantEntity<TKey>`

An entity that implements this interface is tenant-owned and isolated:

```csharp no-compile
public interface ITenantEntity<TKey>
{
    TKey TenantId { get; }   // stamped automatically by the interceptor on insert
}
```

- Implement it directly, or derive from `TenantEntity<TKey>`, which has a `TenantId` with a public setter.
- Leave `TenantId` unset. The EF Core interceptor stamps it from the current tenant on `SaveChanges`. It sets the
  property through EF Core, so `TenantId` can have a private or init-only setter. The interceptor also rejects a new
  entity that already names another tenant.
- Entities that do not implement it are shared by all tenants (product catalogues, reference tables), and are never
  filtered or stamped.

## `ITenantContext<TKey>`

The current tenant, read-only. Inject it into endpoints and services:

```csharp no-compile
public interface ITenantContext<TKey>
{
    ITenantDescriptor<TKey>? CurrentTenant { get; }  // null if none resolved
    ITenantDescriptor<TKey> RequiredTenant { get; }  // CurrentTenant, or throws TenantNotResolvedException
    bool HasTenant { get; }                          // true if a tenant is active
    TKey? CurrentTenantId { get; }                   // CurrentTenant?.TenantId, or default(TKey)
    TTenant? GetCurrentTenant<TTenant>()             // CurrentTenant?.As<TTenant>()
        where TTenant : class, ITenantDescriptor<TKey>;
}
```

Without a tenant, `CurrentTenantId` is `null` only for `string` keys: value-type keys get `Guid.Empty` or `0`
([`CurrentTenantId`](api/tenantry-itenantcontext.md)). Check `HasTenant` to tell "no tenant" apart, or read
`RequiredTenant` where a tenant must be current.

## `ITenantContextSetter<TKey>`

`ITenantContextSetter<TKey>` adds to `ITenantContext<TKey>` the methods that set the current tenant:

```csharp no-compile
public interface ITenantContextSetter<TKey> : ITenantContext<TKey>
{
    IDisposable MakeCurrent(ITenantDescriptor<TKey> tenant);
    IDisposable MakeNoTenantCurrent();
}
```

Most code never calls these methods. In ASP.NET Core the middleware calls `MakeCurrent` once the tenant is resolved.
In console and worker apps, [`ITenantScopeFactory<TKey>`](#itenantscopefactorytkey-and-itenantscopetkey) makes a tenant
current together with a new DI scope. Call `MakeCurrent` yourself only when you need no new scope, with a tenant you
already hold ([Running work as a tenant](non-http-hosts.md#running-work-as-a-tenant)).

`MakeCurrent` returns a handle that restores the previous tenant on dispose. Calls nest: an inner one shadows the
outer tenant, so maintenance code can act as another tenant for a moment:

```csharp
using (tenantContext.MakeCurrent(acme))       // current = Acme, here and in anything this calls or awaits
{
    using (tenantContext.MakeCurrent(globex)) // current = Globex
    {
    }                                         // current = Acme again
}                                             // current = the tenant before, or none
```

`MakeNoTenantCurrent()` does the opposite: code inside it sees no tenant, and disposing it restores the tenant that
was current. The middleware uses it for the rest of a request whose tenant the access validators refused.

## The `AsyncLocal` model

The current tenant belongs to the async flow, not to an object. It flows into code you call and await, never back to
your caller, and concurrent requests never see each other's tenant. `ITenantContext<TKey>` and
`ITenantContextSetter<TKey>` are one singleton over an `AsyncLocal`.

- Make a tenant current (`MakeCurrent`, or `ITenantScopeFactory.CreateScope`) in the method that does the work. A tenant
  made current inside an `async` helper is not current for the helper's caller.
- A `Task.Run(...)` started inside a scope inherits the tenant it had then. For work that runs after the scope is
  disposed, capture the tenant id and run the work by id ([Non-HTTP hosts](non-http-hosts.md)).

EF Core's query filter reads this tenant on every query through the context that runs it, so one compiled query
serves every tenant ([How the query filter stays correct](efcore-integration.md#how-the-query-filter-stays-correct)).
For scopes disposed out of order, see [Details](#disposing-scopes-out-of-order).

## `ITenantScopeFactory<TKey>` and `ITenantScope<TKey>`

Outside a request, `ITenantScopeFactory<TKey>` creates an `ITenantScope<TKey>`: a dependency-injection scope with a
tenant current, so the scoped services resolved from it (such as a `DbContext`) are the tenant's. It works like
`IServiceScopeFactory` and `IServiceScope`.

- `RunInScopeAsync(tenantId, …)` looks the tenant up in the store, refuses a missing or inactive one, and runs your
  work in such a scope.
- `CreateScope(tenant)` opens one for a tenant you already hold.

[Running work as a tenant](non-http-hosts.md#running-work-as-a-tenant) says which to use and what each checks.

## Exceptions

| Exception | Package | Thrown when |
|-----------|---------|-------------|
| `TenantNotResolvedException` | `Tenantry.Core` | Code that needs a current tenant runs without one (an EF Core write, `CurrentTenantConnectionString`, `RequiredTenant`). |
| `TenantNotFoundException` | `Tenantry.Core` | A tenant id is not in the store (`RunInScopeAsync`). It derives from `TenantNotResolvedException` and carries the `TenantId`, so a queue consumer can drop a message for a tenant that no longer exists. |
| `TenantInactiveException` | `Tenantry.Core` | `RunInScopeAsync` names a tenant that `ValidateTenantActivity` refuses. It derives from `TenantNotResolvedException` and carries the `TenantId`, of the application's key type. |
| `TenantIsolationViolationException` | `Tenantry.EfCore` | EF Core would read or write across tenants; `Kind` says which check failed ([`TenantIsolationViolationKind`](api/tenantry-efcore-tenantisolationviolationkind.md) lists them). |

## Registration

Every kind of host uses one entry point, `AddTenantry<TKey>(configure?)` in `Tenantry.Core`, which registers the core
services ([`AddTenantry`](api/microsoft-extensions-dependencyinjection-tenantryservicecollectionextensions.md) lists
them). In the `configure` lambda you add a store, connection strings, EF Core options and, with `Tenantry.AspNetCore`,
resolution and access control. A `DbContext` is isolated where it is registered, with `options.UseTenantry()`. The
registration rules:

- Every builder method returns the builder, so calls chain. Three return it without its key type:
  `UseResolver<TResolver>()`, `ValidateTenantAccess<TValidator>()` and `AddDbContextPerTenantDatabase<TContext>()`.
  Put them last in a chain, or make each call a statement of its own:

  ```csharp
  builder.Services.AddTenantry<Guid>(tenant =>
  {
      tenant.UseResolver<CookieTenantResolver>();
      tenant.ValidateTenantAccess<MembershipValidator>();
      tenant.UseStore<EfCoreTenantStore>();
  });
  ```

- Registration needs no Tenantry `using` directive. `AddTenantry` and the builder methods are extension methods in
  `Microsoft.Extensions.DependencyInjection`. `UseTenantry`, `RequireTenant` and `AllowMissingTenant` are in
  `Microsoft.AspNetCore.Builder`. Types such as `TenantDescriptor<TKey>` are in the `Tenantry` namespace.
- Calling `AddTenantry` again adds to the same registration. It must use the same
  [key type](#the-tenant-key-tkey), and the application registers
  [one store](tenant-stores.md#registration-and-lifetimes).
- Code that needs the key type without naming it, such as a package's registration method, reads it from
  [`ITenantKeyType`](api/tenantry-itenantkeytype.md), whose generic visitor works under Native AOT.

## Details

### Disposing scopes out of order

Disposing a scope closes it, and changes the current tenant only in the flow that disposes it. That flow moves to the
nearest scope still open, or to no tenant, once its innermost scope is closed: when it disposes that scope, or an outer
one after another flow closed the inner one. Disposing an outer scope while an inner one is open only closes it, and
the inner one stays current.

The move stops early at a scope that was opened when the scope around it had already closed, and the flow gets that
closed scope's tenant. This keeps a flow on its tenant after another flow closed the scope it started in: each scope
the flow opens while none of its own is open gives that tenant back when it closes. A flow started inside such a scope
gets that tenant too, when it closes its own scope after that scope has closed. The hub calls of a long-polling
SignalR connection run this way, in the flow of the request that opened the connection.
