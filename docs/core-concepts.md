# Core concepts

Everything in Tenantry is built on a small set of types in the `Tenantry` namespace (package `Tenantry.Core`).
Understanding them makes the EF Core and ASP.NET Core layers obvious.

## The tenant key (`TKey`)

Every Tenantry type is generic over `TKey`, the type of your tenant identifier. The constraint is:

```csharp no-compile
where TKey : IEquatable<TKey>, IParsable<TKey>
```

- `IEquatable<TKey>` lets EF Core translate `tenantId == currentTenantId` into SQL for the query filter.
- `IParsable<TKey>` lets Tenantry turn text, such as the identifier a request carries in a header, route or
  claim, into a `TKey`, with the invariant culture.

`TenantIds` does this the way Tenantry does, for code of your own that carries tenant ids as text: `Format` writes
an id with the invariant culture, and `TryParse` reads one back, refusing text that names no tenant. `IsUnset` is
true for the ids Tenantry reserves for "no tenant": `null`, the key type's default (`Guid.Empty`, `0`) and an
empty string.

`Guid`, `int`, `long`, `string`, and most numeric types satisfy this. Choose one type and use it
everywhere — the same `TKey` flows through your entities, store, `DbContext`, and registration. An application
has one key type: calling `AddTenantry` with a second one throws, and an entity that implements
`ITenantEntity<string>` in a `Guid` application fails its model's first query or save instead of going
unisolated.

The key type's default value (`Guid.Empty`, `0`, and for `string` keys `null` or an empty string) means "no
tenant" to Tenantry, so no tenant may have it: making such a tenant current throws `ArgumentException`, and an
identifier that parses to it names no tenant.

## `ITenantDescriptor<TKey>` — a resolved tenant

A descriptor is the minimal description of a tenant:

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

Implement `ITenantDescriptor<TKey>` on your own type to carry what your application knows about a tenant (its
plan, region, connection string, feature flags…), and return it from your [tenant store](tenant-stores.md).
Tenantry only ever reads `TenantId` and `Name`. It has no tenant status of its own: keep yours on your tenant type
and give Tenantry a check for it with `ValidateTenantActivity` (see
[Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).

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

Both throw `InvalidOperationException`, naming both types, if the tenant is not of the type you ask for: the store
returns another one.

## `ITenantEntity<TKey>` — a tenant-owned entity

Implementing this marker interface is what opts an entity into isolation:

```csharp no-compile
public interface ITenantEntity<TKey>
{
    TKey TenantId { get; }   // stamped automatically by the interceptor on insert
}
```

- Implement it directly, or derive from the convenience base class `TenantEntity<TKey>` which provides
  the `TenantId` property with a public setter.
- **Do not set `TenantId` yourself.** The EF Core interceptor stamps it from the current tenant on
  `SaveChanges`, and rejects a new entity that already names another tenant. It sets the value through EF Core,
  so your entity can give `TenantId` a private or init-only setter.
- Entities that do not implement this interface are global/shared and are never filtered or stamped.

## `ITenantContext<TKey>` — reading the current tenant

This is the read-only view of "who is the tenant right now", and the type you inject into endpoints,
services, and your `DbContext`:

```csharp no-compile
public interface ITenantContext<TKey>
{
    ITenantDescriptor<TKey>? CurrentTenant { get; }  // null if none resolved
    bool HasTenant { get; }                          // true if a tenant is active
    TKey? CurrentTenantId { get; }                   // CurrentTenant?.TenantId, or default(TKey)
    TTenant? GetCurrentTenant<TTenant>()             // CurrentTenant?.As<TTenant>()
        where TTenant : class, ITenantDescriptor<TKey>;
}
```

`CurrentTenantId` is the current tenant's id, the same as `CurrentTenant?.TenantId` (see
[below](#how-ef-core-queries-see-the-current-tenant) for how EF Core reads it). Without a tenant it is `default(TKey)`: `null` for
`string` keys, but `Guid.Empty` or `0` for value-type keys, because `TKey?` on an unconstrained generic is not
nullable for them. Check `HasTenant` to tell "no tenant" apart.

## `ITenantContextSetter<TKey>` — making a tenant current

`ITenantContextSetter<TKey>` extends `ITenantContext<TKey>` with the ability to *set* the current tenant:

```csharp no-compile
public interface ITenantContextSetter<TKey> : ITenantContext<TKey>
{
    IDisposable Use(ITenantDescriptor<TKey> tenant);
}
```

`Use` makes a tenant current and returns a handle that restores the previous tenant on dispose:

```csharp
using (tenantContext.Use(acme))
{
    // ITenantContext.CurrentTenant == acme here, and inside anything this calls/awaits
}
// previous tenant (or "none") restored here
```

In ASP.NET Core the **middleware** calls `Use` for you once the tenant is resolved. In console and worker apps,
`ITenantScopeFactory<TKey>` makes a tenant current together with a fresh DI scope, which is what most code
wants; call `Use` yourself only when you need no new scope. See [Non-HTTP hosts](non-http-hosts.md).

Call `Use` (or `ITenantScopeFactory.CreateScope`) in the method that does the work. Because of the
`AsyncLocal` model below, a tenant made current inside an `async` helper is not current for the helper's caller.

### Uses nest

An inner `Use` shadows the outer tenant and the outer one is restored on dispose:

```csharp
using (tenantContext.Use(acme))       // current = Acme
{
    using (tenantContext.Use(globex)) // current = Globex
    {
    }                                 // current = Acme again
}                                     // current = none
```

This is useful for admin/maintenance code that needs to briefly act as a specific tenant from within
another context.

## The `AsyncLocal` model

`ITenantContext<TKey>` and `ITenantContextSetter<TKey>` are both registered as a **singleton** backed by a
single `AsyncLocal` holding the innermost open scope. The implication matters:

- The "current tenant" is **per async-execution-context**, not per object instance. The value flows
  *down* into every method you call and every `Task` you `await`, but never *up* to your caller.
- This is why a singleton is correct and safe: there is no per-request instance to manage, and the
  value cannot leak between concurrent requests/operations because each runs in its own async context.
- A background `Task.Run(...)` started inside a scope inherits the tenant at the moment it is created.
  If you queue work to run *later* (after the scope disposes), capture the tenant id and open a fresh
  scope when the work runs (`ITenantScopeFactory.RunInScopeAsync`); do not rely on the ambient value
  still being set.
- Disposing a scope is order-safe. Disposing the innermost scope restores the nearest scope that is
  still open; disposing any other scope (out of order, or from a different async flow) closes it without
  changing the active tenant.
- Disposal restores the tenant only in the flow that disposes. If a child task disposes a handle it
  inherited, the caller keeps that tenant until it disposes the handle as well, which then restores the
  caller's previous tenant. Further disposals change nothing.

### How EF Core queries see the current tenant

EF Core compiles a global query filter **once** and caches the plan, but it evaluates the parts of a filter that
read from the `DbContext` again every time a query runs. Tenantry's filter reads the tenant through the context
that runs the query, from its `ITenantContext<TKey>`, so the same cached plan always uses the *current* tenant.
This is covered in depth in [EF Core integration](efcore-integration.md#how-the-query-filter-stays-correct).

## `ITenantScopeFactory<TKey>` and `ITenantScope<TKey>` — work as a tenant

Outside a request, `ITenantScopeFactory<TKey>` creates an `ITenantScope<TKey>`: a dependency-injection scope with a
tenant current, so the scoped services resolved from it (such as a `DbContext`) are the tenant's. It follows the
`IServiceScopeFactory` → `IServiceScope` pattern. `RunInScopeAsync(tenantId, …)` looks the tenant up in the store
and runs your work in such a scope. See [Non-HTTP hosts](non-http-hosts.md).

## Exceptions

| Exception | Package | Thrown when |
|-----------|---------|-------------|
| `TenantNotResolvedException` | `Tenantry.Core` | Code that needs a current tenant runs without one (an EF Core write, `CurrentTenantConnectionString`). |
| `TenantNotFoundException` | `Tenantry.Core` | A tenant id is not in the store (`RunInScopeAsync`). It derives from `TenantNotResolvedException` and carries the `TenantId`, so a queue consumer can drop a message for a tenant that no longer exists. |
| `TenantInactiveException` | `Tenantry.Core` | `RunInScopeAsync` names a tenant that `ValidateTenantActivity` refuses. It derives from `TenantNotResolvedException` and carries the `TenantId`. |
| `TenantIsolationViolationException` | `Tenantry.EfCore` | EF Core would read or write across tenants; `Kind` says which check failed. See [EF Core integration](efcore-integration.md). |

## Registration

There is one entry point, `AddTenantry<TKey>(configure?)` in `Tenantry.Core`, for every kind of host. It registers
the ambient tenant (`ITenantContext<TKey>`, `ITenantContextSetter<TKey>`), `ITenantScopeFactory<TKey>`,
`ITenantLookup<TKey>`, `ITenantStoreCache<TKey>`, `ITenantActivity<TKey>` and `ITenantKeyType`. Inside the
`configure` lambda you add a store, connection strings, EF Core options and, with `Tenantry.AspNetCore`, resolution
and access control. A `DbContext` is isolated where it is registered, with `options.UseTenantry()`.

The rules, stated here once:

- Every builder method returns the builder, so calls chain. Three return it without its key type, so put them last:
  `UseResolver<TResolver>()`, `ValidateTenantAccess<TValidator>()` and `AddDbContextPerTenantDatabase<TContext>()`.
  The first two have overloads that take a `Type` and chain: `UseResolver(typeof(CookieTenantResolver))`.
- Registration needs no Tenantry `using` directive: `AddTenantry` and the builder methods are extension methods in
  `Microsoft.Extensions.DependencyInjection`.
- Calling `AddTenantry` again adds to the same registration. An application uses one tenant key type and one store:
  a second of either throws.
