# EF Core integration

`Tenantry.EfCore` provides the data isolation that makes multi-tenancy real. It has two independent
halves:

- **Read isolation** — a global query filter restricts every query against an `ITenantScoped<TKey>`
  entity to the current tenant.
- **Write isolation** — a `SaveChanges` interceptor stamps `TenantId` on new rows and rejects updates
  and deletes of another tenant's rows before saving; the stored tenant is also part of every `UPDATE`
  and `DELETE` statement, and bulk updates cannot change `TenantId`. Tenant-scoped writes with no tenant
  are rejected by default, and an optional check rejects inserts pre-stamped with a foreign tenant.

Both work on **any** `DbContext` — no base class required — using only standard EF Core features, so
they are provider-agnostic; the test suite covers SQLite and SQL Server. They
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
using Microsoft.EntityFrameworkCore.Infrastructure; // GetService

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), ITenantAwareDbContext<Guid>
{
    private ITenantContext<Guid>? _tenantContext;

    // Resolved from the application service provider on first use, so the constructor takes only the
    // options and the context also works with DbContext pooling.
    public Guid CurrentTenantId =>
        (_tenantContext ??= this.GetService<ITenantContext<Guid>>()).CurrentTenantId;

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
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : MultiTenantDbContext<Guid>(options)
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

Either way, the context only ever *reads* the tenant, through `ITenantContext<TKey>` (never
`ITenantScope<TKey>`). Tenantry registers it as a singleton over ambient per-request state, so one context
instance always sees the tenant that is active when it queries or saves. Contexts created outside dependency
injection can pass an `ITenantContext<TKey>` to `MultiTenantDbContext`'s two-argument constructor instead.

## Read isolation: the global query filter

`ApplyTenantFilters<TKey, TContext>(this)` scans every entity type in the model, and for those that
implement `ITenantScoped<TKey>`:

- adds a global query filter equivalent to
  `entity => context.CurrentTenantId != default && entity.TenantId == context.CurrentTenantId`, and
- marks `TenantId` as a concurrency token, so updates and deletes also match on the stored tenant.

Tenantry does not add indexes. Because every filtered query compares `TenantId`, index it yourself: usually
as the **leading column of composite indexes** that match your queries, rather than on its own.

```csharp
modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.CreatedAt });
modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.Reference }).IsUnique(); // unique per tenant
```

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
- **Modified / Deleted** entities are **validated**: the entity must have been loaded or attached as the
  current tenant and must still belong to it. Otherwise the interceptor throws
  `TenantIsolationViolationException` **before any data is written** and the whole `SaveChanges` is
  aborted. This is **always on**, regardless of configuration.
- **The database enforces ownership too.** `ApplyTenantFilters` marks `TenantId` as a concurrency token,
  so every `UPDATE` and `DELETE` includes `AND TenantId = <tenant the entity was loaded or attached with>`.
  A detached entity that pairs another tenant's primary key with the current tenant's `TenantId` passes
  the in-memory check but matches no row, so EF Core throws `DbUpdateConcurrencyException` and nothing is
  changed. The interceptor logs a warning when a tenant-scoped write matches no row. No schema change is
  needed; your next migration's model snapshot records the concurrency token.

If there is **no resolved tenant**, behaviour follows the `OnMissingTenant` policy (below).

`TenantIsolationViolationException` carries `EntityTypeName`, `OffendingTenantId`, and
`ExpectedTenantId` for diagnostics and lives in `Tenantry.Core.Exceptions`.

## Configuring write isolation

```csharp
tenant.AddEfCoreIsolation(options =>
{
    options.OnMissingTenant = MissingTenantBehavior.Reject; // default: Reject
    options.DetectSpoofedWrites = false;                    // default: false
});
```

### `OnMissingTenant` — what happens when a write runs with no tenant

The policy applies only when a save writes entities that implement `ITenantScoped<TKey>`. Saves that
write only host-level data (the tenant registry, a global catalogue, seeding reference data) never
need a tenant and are unaffected. The `MissingTenantBehavior` values:

| Value | Behaviour when tenant-scoped entities are saved with no tenant |
|-------|----------------------------------------|
| `Reject` *(default)* | Throws `TenantNotResolvedException` before anything is persisted. |
| `Warn` | The save proceeds and a structured warning is logged. |
| `Allow` | The save proceeds silently. |

`Warn` and `Allow` are opt-ins for maintenance code that deliberately writes across tenants. Updates and
deletes are then not tenant-checked, and a new entity must set `TenantId` explicitly: an unowned row is
always rejected, whatever the policy. Prefer running maintenance per tenant inside
`ITenantScope.BeginScope` instead. `Skip` exists for background-job propagation and is rejected here.

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
EF Core walks anyway), and keep `OnMissingTenant` at `Reject` except in maintenance code.

## DbContext pooling

Pooled contexts are supported. A pooled instance is reused across requests, and because the context reads
the ambient tenant on every query and save, each request sees only its own tenant. Two rules apply:

- The context must have a single constructor that takes only its options (both options above do).
- Call `AddTenantInterceptors(sp)` in the registration callback. EF Core does not let `OnConfiguring`
  change a pooled context's options, so `MultiTenantDbContext` cannot attach them itself; if you forget,
  the first use fails with an EF Core error about `OnConfiguring` and pooling rather than saving without
  isolation.

```csharp
builder.Services.AddDbContextPool<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString).AddTenantInterceptors(sp));

// or, for IDbContextFactory<AppDbContext>
builder.Services.AddPooledDbContextFactory<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString).AddTenantInterceptors(sp));
```

This covers shared-database isolation. Pooling with a database per tenant needs the connection switched
for each lease, which Tenantry Pro handles separately.

## What is and isn't isolated

Tenantry isolates tenants in the application, through EF Core's query pipeline and `SaveChanges`. It is
**not** database-enforced row-level security: anything that reaches the database outside those paths is
not tenant-checked. If you need the database itself to enforce isolation (for example against direct SQL
access), add row-level security policies in the database as well, or use a database per tenant.

| Operation | Isolated? | Behaviour |
|-----------|-----------|-----------|
| LINQ queries | Yes | The query filter limits results to the current tenant; with no tenant they match nothing. |
| `SaveChanges` insert, update, delete | Yes | Inserts are stamped; updates and deletes must belong to the current tenant, checked in memory and in the SQL `WHERE` clause. Without a tenant, `OnMissingTenant` applies. |
| `ExecuteUpdate`, `ExecuteDelete` | Yes | The query filter limits affected rows to the current tenant; with no tenant they affect nothing. `ExecuteUpdate` may not set `TenantId` and throws `TenantIsolationViolationException` if it tries. |
| `IgnoreQueryFilters()` | No, by design | Removes the tenant filter from that query, including `ExecuteUpdate`/`ExecuteDelete`, which then affect **every** tenant. Treat it as a privileged operation. |
| Raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`) | No | Neither the filter nor the interceptors see raw SQL. Add the tenant predicate yourself. |
| Pooled contexts | Yes | Each use reads the tenant active at that moment; see [DbContext pooling](#dbcontext-pooling). |
| Other `DbContext` instances | No | Contexts created without `AddTenantInterceptors` (or not deriving from `MultiTenantDbContext`) get no write isolation. The interceptor logs a warning when a tenant-scoped entity's `TenantId` is not a concurrency token, which means `ApplyTenantFilters` was not called. |

## Migrations

The tenant filter and the `TenantId` concurrency token are part of the model, so they participate in
migrations normally (neither changes the schema):

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

A couple of notes:

- The `TenantId` column comes from your entity (via `ITenantScoped<TKey>`/`TenantScoped<TKey>`). Add
  the indexes your queries need (see above); `ApplyTenantFilters` does not create any.
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
