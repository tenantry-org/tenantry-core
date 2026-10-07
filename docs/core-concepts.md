# Core concepts

Tenantry is built on a few types in the `Tenantry` namespace (package `Tenantry.Core`).

## The tenant key (`TKey`)

Tenantry's tenant types are generic over `TKey`, the type of your tenant identifier. The constraint is:

```csharp no-compile
where TKey : IEquatable<TKey>, IParsable<TKey>
```

- `IEquatable<TKey>` lets EF Core translate `tenantId == currentTenantId` into SQL for the query filter.
- `IParsable<TKey>` lets Tenantry turn text, such as the identifier a request carries in a header, route or
  claim, into a `TKey`, with the invariant culture.

`Guid`, `int`, `long`, `string` and most numeric types satisfy this. An application has one key type, used by its
entities, store and registration: calling `AddTenantry` with a second one throws, and an entity that implements
`ITenantEntity<string>` in a `Guid` application fails its model's first query or save instead of going unisolated.

The key type's default value (`Guid.Empty`, `0`, and for `string` keys `null` or an empty string) means "no tenant",
so no tenant may have it: making such a tenant current throws `ArgumentException`, and an identifier that parses to it
names no tenant.

For code of your own that carries tenant ids as text, `TenantIds` does what Tenantry does: `Format` writes an id with
the invariant culture, `TryParse` reads one back and refuses text that names no tenant, and `IsReserved` is true for
the "no tenant" ids.

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
only `TenantId` and `Name`. It has no tenant status of its own: keep yours on your tenant type and give Tenantry a
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

Both throw `InvalidOperationException`, naming both types, if the store returned a tenant of another type.

## `ITenantEntity<TKey>`

An entity that implements this interface is tenant-owned and isolated:

```csharp no-compile
public interface ITenantEntity<TKey>
{
    TKey TenantId { get; }   // stamped automatically by the interceptor on insert
}
```

- Implement it directly, or derive from `TenantEntity<TKey>`, which has a `TenantId` with a public setter.
- Leave `TenantId` unset. The EF Core interceptor stamps it from the current tenant on `SaveChanges`, through EF Core,
  so it can have a private or init-only setter, and rejects a new entity that already names another tenant.
- Entities that do not implement it are shared by all tenants and are never filtered or stamped.

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

Without a tenant, `CurrentTenantId` is `default(TKey)`: `null` for `string` keys, but `Guid.Empty` or `0` for
value-type keys, as `TKey?` on an unconstrained generic is not nullable for them. Check `HasTenant` to tell "no
tenant" apart, or read `RequiredTenant` where a tenant must be current.

## `ITenantContextSetter<TKey>`

`ITenantContextSetter<TKey>` adds to `ITenantContext<TKey>` the methods that set the current tenant:

```csharp no-compile
public interface ITenantContextSetter<TKey> : ITenantContext<TKey>
{
    IDisposable MakeCurrent(ITenantDescriptor<TKey> tenant);
    IDisposable MakeNoTenantCurrent();
}
```

`MakeCurrent` makes a tenant current and returns a handle that restores the previous tenant on dispose:

```csharp
using (tenantContext.MakeCurrent(acme))
{
    // ITenantContext.CurrentTenant == acme here, and inside anything this calls/awaits
}
// previous tenant (or "none") restored here
```

In ASP.NET Core the middleware calls `MakeCurrent` once the tenant is resolved. In console and worker apps,
`ITenantScopeFactory<TKey>` makes a tenant current together with a new DI scope, which is what most code wants; call
`MakeCurrent` yourself only when you need no new scope.

`MakeCurrent` does not look the tenant up or check that it is active, so pass it a tenant you already hold
([Running work as a tenant](non-http-hosts.md#running-work-as-a-tenant)).

`MakeNoTenantCurrent()` does the opposite: code inside it sees no tenant, and disposing it restores the tenant that
was current. The middleware uses it for the rest of a request whose tenant the access validators refused.

### Nesting

An inner `MakeCurrent` shadows the outer tenant, which is restored on dispose. Maintenance code can use this to act as
another tenant for a moment:

```csharp
using (tenantContext.MakeCurrent(acme))       // current = Acme
{
    using (tenantContext.MakeCurrent(globex)) // current = Globex
    {
    }                                         // current = Acme again
}                                             // current = none
```

## The `AsyncLocal` model

`ITenantContext<TKey>` and `ITenantContextSetter<TKey>` are one singleton over an `AsyncLocal`, so the current tenant
belongs to the async flow, not to an object. It flows into code you call and await, never back to your caller, and
concurrent requests never see each other's tenant.

- Make a tenant current (`MakeCurrent`, or `ITenantScopeFactory.CreateScope`) in the method that does the work. A tenant
  made current inside an `async` helper is not current for the helper's caller.
- A `Task.Run(...)` started inside a scope inherits the tenant it had then. For work that runs after the scope is
  disposed, capture the tenant id and run the work by id (see [Non-HTTP hosts](non-http-hosts.md)).
- Disposing the innermost scope restores the nearest one still open. Disposing any other, out of order or from
  another async flow, closes it without changing the current tenant, and only in the flow that disposes it.
- A flow that runs on after another flow closed the scope it started in keeps that scope's tenant, and disposing a
  scope it opened after that restores it. The hub calls of a long-polling SignalR connection run this way, in the flow
  of the request that opened the connection.

EF Core's query filter reads this tenant on every query through the context that runs it, so one compiled query
serves every tenant ([How the query filter stays correct](efcore-integration.md#how-the-query-filter-stays-correct)).

## `ITenantScopeFactory<TKey>` and `ITenantScope<TKey>`

Outside a request, `ITenantScopeFactory<TKey>` creates an `ITenantScope<TKey>`: a dependency-injection scope with a
tenant current, so the scoped services resolved from it (such as a `DbContext`) are the tenant's. It works like
`IServiceScopeFactory` and `IServiceScope`. `RunInScopeAsync(tenantId, …)` looks the tenant up in the store, refuses a
missing or inactive one, and runs your work in such a scope. `CreateScope(tenant)` opens one for a tenant you already
hold and, like `MakeCurrent`, checks nothing ([Non-HTTP hosts](non-http-hosts.md#running-work-as-a-tenant)).

## Exceptions

| Exception | Package | Thrown when |
|-----------|---------|-------------|
| `TenantNotResolvedException` | `Tenantry.Core` | Code that needs a current tenant runs without one (an EF Core write, `CurrentTenantConnectionString`, `RequiredTenant`). |
| `TenantNotFoundException` | `Tenantry.Core` | A tenant id is not in the store (`RunInScopeAsync`). It derives from `TenantNotResolvedException` and carries the `TenantId`, so a queue consumer can drop a message for a tenant that no longer exists. |
| `TenantInactiveException` | `Tenantry.Core` | `RunInScopeAsync` names a tenant that `ValidateTenantActivity` refuses. It derives from `TenantNotResolvedException` and carries the `TenantId`, of the application's key type. |
| `TenantIsolationViolationException` | `Tenantry.EfCore` | EF Core would read or write across tenants; `Kind` says which check failed. See [EF Core integration](efcore-integration.md). |

## Registration

Every kind of host uses one entry point, `AddTenantry<TKey>(configure?)` in `Tenantry.Core`. It registers the ambient
tenant (`ITenantContext<TKey>`, `ITenantContextSetter<TKey>`), `ITenantScopeFactory<TKey>`, `ITenantLookup<TKey>`,
`ITenantInvalidator<TKey>`, `ITenantActivity<TKey>` and `ITenantKeyType`. In the `configure` lambda you add a store,
connection strings, EF Core options and, with `Tenantry.AspNetCore`, resolution and access control. A `DbContext` is
isolated where it is registered, with `options.UseTenantry()`. The registration rules:

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

- Registration needs no Tenantry `using` directive: `AddTenantry` and the builder methods are extension methods in
  `Microsoft.Extensions.DependencyInjection`.
- Calling `AddTenantry` again adds to the same registration. An application uses one tenant key type and one store:
  a second of either throws.
- Code that needs the key type without naming it, such as a package's registration method, reads it with
  `services.FindTenantKeyType()` while registering, or injects `ITenantKeyType` later. Its `Accept` method calls a
  generic visitor with the key type, which works under Native AOT.
