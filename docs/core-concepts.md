# Core concepts

Everything in Tenantry is built on a small set of types in `Tenantry.Core`. Understanding them makes
the EF Core and ASP.NET Core layers obvious.

## The tenant key (`TKey`)

Every Tenantry type is generic over `TKey`, the type of your tenant identifier. The constraint is:

```csharp
where TKey : IEquatable<TKey>, IParsable<TKey>
```

- `IEquatable<TKey>` lets EF Core translate `tenantId == currentTenantId` into SQL for the query filter.
- `IParsable<TKey>` lets resolvers turn the raw `string` from a header/route/claim into a `TKey` via
  `TKey.TryParse`.

`Guid`, `int`, `long`, `string`, and most numeric types satisfy this. Choose one type and use it
everywhere — the same `TKey` flows through your entities, store, `DbContext`, and registration. Mixing
key types (e.g. registering `AddTenantry<Guid>` but writing `ITenantScoped<string>` entities) means
the two never line up and isolation silently does nothing for those entities.

## `ITenantDescriptor<TKey>` — a resolved tenant

A descriptor is the minimal description of a tenant:

```csharp
public interface ITenantDescriptor<out TKey>
{
    TKey TenantId { get; }   // used for data isolation
    string Name { get; }     // human-readable display name
}
```

`TenantDescriptor<TKey>` is the default implementation:

```csharp
new TenantDescriptor<Guid> { TenantId = id, Name = "Acme" };
```

You can implement `ITenantDescriptor<TKey>` on your own type to carry extra metadata (subscription
tier, connection string, feature flags…). Your [tenant store](tenant-stores.md) returns whatever
implementation you like; Tenantry only ever reads `TenantId` and `Name`.

## `ITenantScoped<TKey>` — a tenant-owned entity

Implementing this marker interface is what opts an entity into isolation:

```csharp
public interface ITenantScoped<TKey>
{
    TKey TenantId { get; set; }   // stamped automatically by the interceptor on insert
}
```

- Implement it directly, or derive from the convenience base class `TenantScoped<TKey>` which provides
  the `TenantId` property.
- **Do not set `TenantId` yourself.** The EF Core interceptor stamps it from the active tenant scope
  on `SaveChanges`. Setting it manually to the wrong value is exactly what strict mode rejects.
- Entities that do not implement this interface are global/shared and are never filtered or stamped.

## `ITenantContext<TKey>` — reading the current tenant

This is the read-only view of "who is the tenant right now", and the type you inject into endpoints,
services, and your `DbContext`:

```csharp
public interface ITenantContext<out TKey>
{
    ITenantDescriptor<TKey>? CurrentTenant { get; }  // null if none resolved
    bool HasTenant { get; }                          // true if a tenant is active
    TKey? CurrentTenantId { get; }                   // CurrentTenant?.TenantId
}
```

`CurrentTenantId` exists as a separate single-step property specifically for EF Core query filters
(see [below](#why-currenttenantid-is-its-own-property)).

## `ITenantScope<TKey>` — entering a tenant scope

`ITenantScope<TKey>` extends `ITenantContext<TKey>` with the ability to *set* the current tenant:

```csharp
public interface ITenantScope<TKey> : ITenantContext<TKey>
{
    IDisposable BeginScope(ITenantDescriptor<TKey> tenant);
}
```

`BeginScope` activates a tenant and returns a handle that restores the previous tenant on dispose:

```csharp
using (scope.BeginScope(acme))
{
    // ITenantContext.CurrentTenant == acme here, and inside anything this calls/awaits
}
// previous tenant (or "none") restored here
```

In ASP.NET Core the **middleware** calls `BeginScope` for you once the tenant is resolved. In console
and worker apps **you** call it. See [Non-HTTP hosts](non-http-hosts.md).

### Scopes nest

An inner scope shadows the outer tenant and the outer one is restored on dispose:

```csharp
using (scope.BeginScope(acme))     // current = Acme
{
    using (scope.BeginScope(globex)) // current = Globex
    {
    }                                // current = Acme again
}                                    // current = none
```

This is useful for admin/maintenance code that needs to briefly act as a specific tenant from within
another context.

## The `AsyncLocal` model

`ITenantContext<TKey>` and `ITenantScope<TKey>` are both registered as a **singleton** backed by a
single `AsyncLocal<ITenantDescriptor<TKey>?>`. The implication matters:

- The "current tenant" is **per async-execution-context**, not per object instance. The value flows
  *down* into every method you call and every `Task` you `await`, but never *up* to your caller.
- This is why a singleton is correct and safe: there is no per-request instance to manage, and the
  value cannot leak between concurrent requests/operations because each runs in its own async context.
- A background `Task.Run(...)` started inside a scope inherits the tenant at the moment it is created.
  If you queue work to run *later* (after the scope disposes), capture the tenant id and open a fresh
  scope when the work runs — do not rely on the ambient value still being set.

### Why `CurrentTenantId` is its own property

EF Core compiles a global query filter **once** and caches the plan, but it re-evaluates property
accesses **on the `DbContext`** every time a query runs. Tenantry's filter therefore closes over the
`DbContext` and reads `CurrentTenantId` from it per query — so the same cached plan always uses the
*current* tenant. Reading a single, side-effect-free property keeps that per-query evaluation cheap.
This is covered in depth in [EF Core integration](efcore-integration.md#how-the-query-filter-stays-correct).

## Registration entry points

| Method | Package | Use for |
|--------|---------|---------|
| `AddTenantryCore<TKey>(configure?)` | `Tenantry.Core` | Console apps, workers, desktop UIs — registers the context/scope and lets you add isolation. No HTTP resolution. |
| `AddTenantry<TKey>(configure)` | `Tenantry.AspNetCore` | ASP.NET Core — calls `AddTenantryCore` internally, then adds resolvers, middleware wiring, and startup validation. |

All registrations are idempotent: calling both is safe, and the core services are only added once.
Inside the `configure` lambda you compose stores (`UseInMemoryStore`, `UseStore`), isolation
(`AddEfCoreIsolation`), and — for ASP.NET Core — resolution and access control.
