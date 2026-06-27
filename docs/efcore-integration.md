# EF Core integration

`Tenantry.EfCore` provides the data isolation that makes multi-tenancy real. It has two independent
halves:

- **Read isolation** — a global query filter restricts every query against an `ITenantScoped<TKey>`
  entity to the current tenant.
- **Write isolation** — a `SaveChanges` interceptor stamps `TenantId` on new rows and rejects
  cross-tenant writes before they reach the database. A configurable policy controls what happens when
  a write runs with no tenant, and an optional check rejects inserts pre-stamped with a foreign tenant.

Both work on **any** `DbContext` — no base class required — and on **any** relational provider. They
are driven by the same `ITenantContext<TKey>` used everywhere else, so HTTP and non-HTTP hosts behave
identically.

## Setup at a glance

```csharp
// 1. Register isolation services inside AddTenantry / AddTenantryCore
builder.Services.AddTenantry<Guid>(tenant =>
{
    tenant.ResolveFromHeader("X-Tenant-Id");
    tenant.UseInMemoryStore(tenants);
    tenant.AddEfCoreIsolation(options =>
    {
        options.OnMissingTenant = MissingTenantBehavior.Reject;   // reject writes with no tenant
        options.DetectSpoofedWrites = true;                       // reject inserts with a foreign tenant id
    });
});

// 2. Attach the interceptor to your DbContext
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddTenantInterceptors(sp));   // throws if AddEfCoreIsolation() wasn't called
```

```csharp
// 3. Mark entities tenant-scoped
public class Order : TenantScoped<Guid> { public int Id { get; set; } /* … */ }

// 4. Apply the query filters in the DbContext (see "wiring the DbContext" below)
```

`AddEfCoreIsolation` registers the interceptor and the configured isolation policy (see
[write isolation](#write-isolation-the-interceptor) below). `AddTenantInterceptors(sp)` is what actually
adds the interceptor to that specific `DbContext`'s options — call it in every `AddDbContext` you want
isolated.

## Choosing how to wire the DbContext

The query filter needs to read the current tenant id from the `DbContext`. There are two ways to set
that up; they produce identical behaviour.

### Option A — implement `ITenantAwareDbContext<TKey>` (works with any existing context)

```csharp
public class AppDbContext : DbContext, ITenantAwareDbContext<Guid>
{
    private readonly ITenantContext<Guid> _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext<Guid> tenantContext)
        : base(options) => _tenantContext = tenantContext;

    public Guid? CurrentTenantId => _tenantContext.CurrentTenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyTenantFilters<Guid, AppDbContext>(this);
    }
}
```

Use this when you have an existing `DbContext` or a required base class you cannot change.

### Option B — derive from `MultiTenantDbContext<TKey>` (greenfield convenience)

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext<Guid> ctx)
    : MultiTenantDbContext<Guid>(options, ctx)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);   // implements ITenantAwareDbContext + applies filters
        // … your entity configuration
    }
}
```

The base class implements `ITenantAwareDbContext<TKey>` and calls `ApplyTenantFilters` for you. Always
call `base.OnModelCreating(modelBuilder)` **first**.

Either way, inject `ITenantContext<TKey>` — never `ITenantScope<TKey>` — into the context. The context
only ever *reads* the tenant.

## Read isolation: the global query filter

`ApplyTenantFilters<TKey, TContext>(this)` scans every entity type in the model, and for those that
implement `ITenantScoped<TKey>`:

- adds a global query filter equivalent to
  `entity => context.CurrentTenantId != default && entity.TenantId == context.CurrentTenantId`, and
- configures an index on the `TenantId` column for query performance.

So a plain `db.Orders.ToListAsync()` returns only the current tenant's rows — you never write
`Where(o => o.TenantId == …)` by hand. Entities without `ITenantScoped<TKey>` are untouched and remain
global.

### Fail-closed behaviour

Note the `context.CurrentTenantId != default` guard. When **no tenant is resolved**, `CurrentTenantId`
is the default value (`Guid.Empty`, `0`, `null`…) and the filter matches **nothing**. Reads return zero
rows rather than leaking every tenant's data. This is deliberate: a missing tenant is treated as "see
nothing", not "see everything".

> Edge case: if a real tenant could legitimately have the default key value (e.g. `0` for an `int`
> key, or `Guid.Empty`), the guard would hide its rows. Avoid using the default value as a real tenant
> id.

### How the query filter stays correct

EF Core compiles a global query filter **once** and caches the plan across all instances of the model.
If the filter closed over an injected `ITenantContext<TKey>` service, that service would be captured as
a constant at compile time and every query would use whichever tenant happened to be active when the
plan was first built — a serious leak.

Tenantry avoids this by closing the filter over the **`DbContext` instance** and reading
`CurrentTenantId` off it. EF Core re-evaluates `DbContext` member accesses on **every** query
execution, so the cached plan always reads the *current* tenant. That is the entire reason
`ITenantAwareDbContext<TKey>.CurrentTenantId` exists and why your context delegates it to the injected
`ITenantContext<TKey>`.

This applies to **global query filters** specifically. Inline `.Where(...)` clauses are evaluated per
execution anyway, so they are not affected.

### Combining with your own query filters

`ApplyTenantFilters` is idempotent and combines the tenant filter with any filter you have already
configured on an entity (with logical AND). On **EF Core 10+** it uses *keyed* query filters so the
tenant filter is registered independently of yours; on earlier versions it merges the expressions. You
can configure your own soft-delete or status filters normally and the tenant filter is added on top.

### Bypassing the filter (admin / reporting)

Use EF Core's standard `IgnoreQueryFilters()` to deliberately cross tenant boundaries — for admin
dashboards, cross-tenant reports, or maintenance:

```csharp
var perTenant = await db.Orders
    .IgnoreQueryFilters()
    .GroupBy(o => o.TenantId)
    .Select(g => new { Tenant = g.Key, Count = g.Count() })
    .ToListAsync();
```

This bypasses the read filter only. Use it consciously and guard such endpoints with appropriate
authorization — it is the one place the isolation is intentionally off.

## Write isolation: the interceptor

The `SaveChanges`/`SaveChangesAsync` interceptor runs on every save against a context with
`AddTenantInterceptors`, and for entities implementing `ITenantScoped<TKey>`:

- **Added** entities have their `TenantId` **stamped** from the current tenant — overwriting whatever
  was set (unless `DetectSpoofedWrites` is on; see below).
- **Modified / Deleted** entities are **validated**: if an entity belongs to a different tenant than
  the current scope, the interceptor throws `TenantIsolationViolationException` **before any data is
  written** and the whole `SaveChanges` is aborted. This is **always on**, regardless of configuration.

If there is **no resolved tenant**, behaviour follows the `OnMissingTenant` policy (below).

`TenantIsolationViolationException` carries `EntityTypeName`, `OffendingTenantId`, and
`ExpectedTenantId` for diagnostics and lives in `Tenantry.Core.Exceptions`.

## Configuring write isolation

```csharp
tenant.AddEfCoreIsolation(options =>
{
    options.OnMissingTenant = MissingTenantBehavior.Warn;   // default: Warn
    options.DetectSpoofedWrites = false;                    // default: false
});
```

### `OnMissingTenant` — what happens when a write runs with no tenant

Some saves legitimately run outside a tenant (seeding global data, the tenant registry itself), so the
default does not throw — but you can tighten it. The `MissingTenantBehavior` values:

| Value | Behaviour on an unscoped `SaveChanges` |
|-------|----------------------------------------|
| `Allow` | Save proceeds, nothing stamped, no log. |
| `Warn` *(default)* | Save proceeds, nothing stamped, a structured warning is logged — surfaces endpoints or jobs that bypassed tenant propagation. |
| `Reject` | Throws `TenantNotResolvedException` before anything is persisted. Use this when no write should ever run without a tenant. |
| `Skip` | Treated as `Allow` for writes (it exists for background-job propagation, where it means "drop the job"). |

Reads are unaffected by this setting — they always fail closed (a query with no tenant matches nothing).

### `DetectSpoofedWrites` — reject inserts pre-stamped with a foreign tenant

By default, an `Added` entity with an explicitly set, *wrong* `TenantId` is silently overwritten with
the correct one. With `DetectSpoofedWrites = true`, the validator inspects `Added`, `Modified`, and
`Deleted` entries and **throws** `TenantIsolationViolationException` if an entity carries a tenant id
that is neither unset nor the current tenant — catching code (or a malicious payload) trying to write
to another tenant. (An `Added` entity with an *unset* id is fine; the interceptor stamps it.) "Unset"
treats both `null` and `string.Empty` as not-yet-assigned, because string-keyed entities are commonly
initialised to `string.Empty`.

Recommendation: set `DetectSpoofedWrites = true` (negligible overhead — a pass over the change tracker
EF Core walks anyway), and tighten `OnMissingTenant` to `Reject` for services where every write must be
tenant-scoped.

## Migrations

The tenant filter and the `TenantId` index are part of the model, so they participate in migrations
normally:

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

A couple of notes:

- The `TenantId` column comes from your entity (via `ITenantScoped<TKey>`/`TenantScoped<TKey>`); the
  index on it is added by `ApplyTenantFilters`. Generate migrations after wiring the filters so the
  index is captured.
- Design-time tooling (`dotnet ef`) constructs your `DbContext` without a real tenant. That is fine —
  the filter's fail-closed guard simply means design-time has "no tenant", which does not affect schema
  generation.

The [`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) uses real migrations, a database-backed
tenant store, mixed tenanted/global entities, cross-boundary relationships, and an admin endpoint.

## Non-HTTP usage

In console apps, workers, and background jobs there is no middleware to open the scope. Register with
`AddTenantryCore`, attach the interceptor exactly as above, and call `BeginScope` yourself around your
unit of work. The runnable [`EfCoreConsole` sample](../samples/Tenantry.Samples.EfCoreConsole) shows
stamping, read filtering, nested scopes, strict-mode rejection, and fail-closed reads. See
[Non-HTTP hosts](non-http-hosts.md).
