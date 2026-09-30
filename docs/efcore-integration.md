# EF Core integration

`Tenantry.EfCore` provides the data isolation that makes multi-tenancy real. It has two independent
halves:

- **Read isolation** — a global query filter restricts every query against an `ITenantEntity<TKey>`
  entity to the current tenant.
- **Write isolation** — a `SaveChanges` interceptor stamps `TenantId` on new rows and rejects updates
  and deletes of another tenant's rows before saving; the stored tenant is also part of every `UPDATE`
  and `DELETE` statement, and bulk updates cannot change `TenantId`. Tenant-scoped writes with no tenant
  are rejected by default, and an optional check rejects inserts pre-stamped with a foreign tenant.

Both work on **any** `DbContext` — no base class required — using only standard EF Core features, so
they are provider-agnostic; see [tested providers](#tested-providers) for what the test suite covers. They
are driven by the same `ITenantContext<TKey>` used everywhere else, so HTTP and non-HTTP hosts behave
identically.

## Setup at a glance

> **Introductory setup.** Resolving the tenant from a header without authentication lets any caller
> select any tenant. Use it to learn the API. For production, authenticate callers and validate that
> they belong to the tenant they select, as in the [`SecureApi` sample](../samples/Tenantry.Samples.SecureApi).

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;
using Tenantry.EfCore;

// 1. Register isolation services inside AddTenantry
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseInMemoryStore(tenants)
    .AddEfCoreIsolation());

// 2. Attach the interceptor to your DbContext
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddTenantInterceptors(sp));   // throws if AddEfCoreIsolation() wasn't called
```

```csharp
// 3. Mark entities tenant-scoped
public class Order : TenantEntity<Guid>
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

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
        // … your entity configuration, including your own query filters
        modelBuilder.ApplyTenantFilters<Guid, AppDbContext>(this); // last
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
        // … your entity configuration, including your own query filters
        base.OnModelCreating(modelBuilder);   // last: applies the tenant filters
    }
}
```

The base class implements `ITenantAwareDbContext<TKey>` and calls `ApplyTenantFilters` for you. Call
`base.OnModelCreating(modelBuilder)` **last**, after your own configuration (see
[Combining with your own query filters](#combining-with-your-own-query-filters)).

Either way, the context only ever *reads* the tenant, through `ITenantContext<TKey>` (never
`ITenantContextSetter<TKey>`). Tenantry registers it as a singleton over ambient per-request state, so one context
instance always sees the tenant that is active when it queries or saves. Contexts created outside dependency
injection can pass an `ITenantContext<TKey>` to `MultiTenantDbContext`'s two-argument constructor instead.

## Read isolation: the global query filter

`ApplyTenantFilters<TKey, TContext>(this)` scans every entity type in the model, and for those that
implement `ITenantEntity<TKey>`:

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
`Where(o => o.TenantId == …)` by hand. Entities without `ITenantEntity<TKey>` are untouched and remain
global.

In an inheritance hierarchy, EF Core filters through the root entity type, so the root's tenant filter
covers the derived types; a tenant-scoped type whose base entity type is not tenant-scoped throws
`TenantIsolationViolationException`. EF Core reads an owned type's rows only through its owner and does not
let it have a filter of its own, so a tenant-scoped owned type needs a tenant-scoped owner (otherwise it
throws); its `TenantId` is still a concurrency token. An entity that implements `ITenantEntity` with a key
type other than the one in use also throws, because nothing would isolate it.

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
configured on an entity (with logical AND). On **EF Core 10+** it adds a *named* query filter, registered
independently of your named filters; on earlier versions, and with an unnamed filter on EF Core 10, it
merges the expressions.

Call it (or `base.OnModelCreating` in a `MultiTenantDbContext`) **at the end of `OnModelCreating`**,
after your own configuration:

- An entity type added after it gets no tenant filter.
- A `HasQueryFilter` call after it can replace the tenant filter: before EF Core 10 any call does, and on EF
  Core 10 an unnamed one does when `ApplyTenantFilters` merged the tenant filter into your unnamed filter
  (otherwise it fails the model build).

The tenant interceptors check the model on its first query and its first save. If a tenant-scoped entity
type has lost its tenant filter or its `TenantId` concurrency token, they throw
`TenantIsolationViolationException` instead of running the query or the save, which would otherwise read
every tenant's rows. Only the filter `ApplyTenantFilters` adds counts: a tenant filter written by hand, or
one a model-building convention or `IModelCustomizer` sets after `OnModelCreating`, is not recognised. A
context that attaches the interceptors must therefore apply the tenant filters; to read across tenants on
purpose, use `IgnoreQueryFilters()` (see below) rather than leaving the filters out.

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
`AddTenantInterceptors`, and for entities implementing `ITenantEntity<TKey>`:

- **Added** entities have their `TenantId` **stamped** from the current tenant when it is unset (the key
  type's default, `null` or `string.Empty`). An `Added` entity that already names **another** tenant is
  rejected with `TenantIsolationViolationException` rather than silently moved, which catches code (or a
  request body) trying to write into another tenant. The stamp goes through EF Core, so `TenantId` may have a
  private or init-only setter.
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

`TenantIsolationViolationException` (namespace `Tenantry.EfCore`) says which check failed in `Kind`:

| `Kind` | Thrown when | `TypeName` | Tenant ids |
|--------|-------------|------------|------------|
| `EntityWrite` | `SaveChanges` would write another tenant's entity (above) | the entity | the entity's and the current tenant |
| `BulkUpdate` | An `ExecuteUpdate` would set `TenantId`, or sets a property the guard cannot identify | the entity | `null` |
| `TenantDatabaseMismatch` | A pooled database-per-tenant context would use another tenant's database ([below](#pooling-with-a-database-per-tenant)) | the `DbContext` | the database's and the current tenant (`null` when none) |
| `ModelConfiguration` | A model does not isolate a tenant-scoped entity type ([below](#what-is-and-isnt-isolated)) | the entity | `null` |

`OffendingTenantId` and `ExpectedTenantId` are strings for logging. Nothing has been written when it is thrown.

## Configuring write isolation

```csharp
using Tenantry.EfCore;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .AddEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Reject)); // the default
```

### `OnMissingTenant` — what happens when a write runs with no tenant

The policy applies only when a save writes entities that implement `ITenantEntity<TKey>`. Saves that
write only host-level data (the tenant registry, a global catalogue, seeding reference data) never
need a tenant and are unaffected. The `MissingTenantBehavior` values:

| Value | Behaviour when tenant-scoped entities are saved with no tenant |
|-------|----------------------------------------|
| `Reject` *(default)* | Throws `TenantNotResolvedException` before anything is persisted. |
| `Warn` | The save proceeds and a structured warning is logged. |
| `Allow` | The save proceeds silently. |

`Warn` and `Allow` are opt-ins for maintenance code that deliberately writes across tenants. Updates and
deletes are then not tenant-checked, and a new entity must set `TenantId` explicitly: an unowned row is
always rejected, whatever the policy. Prefer running maintenance per tenant with `ITenantScopeFactory`
instead.

Reads are unaffected by this setting — they always fail closed (a query with no tenant matches nothing).

Keep `OnMissingTenant` at `Reject` except in maintenance code.

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
for each lease; see [Database per tenant](#database-per-tenant).

## Database per tenant

To give each tenant its own database (or route tenants to different servers), tell Tenantry how to find a
tenant's connection string, and read the current tenant's when each context is created:

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true")
    .AddEfCoreIsolation());

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<CurrentTenantConnectionString<string>>().Get())
           .AddTenantInterceptors(sp));
```

`UseConnectionStrings` registers two singletons. `ITenantConnectionStringProvider<TKey>` returns a given tenant's
connection string (`Get(tenant)`, `GetAsync(tenant)`), for code that visits tenants without making each one
current; implement it to decorate the default provider, for example to cache. `CurrentTenantConnectionString<TKey>`
returns the current tenant's (`Get()`, `GetAsync()`) and throws `TenantNotResolvedException` without one. Set
`GetConnectionStringAsync` when the string comes from a secrets store: `GetAsync` prefers it, and the synchronous
`Get` then needs `GetConnectionString` as well. The default provider calls your delegate every time and does not
cache.

Keep deriving from `MultiTenantDbContext` (or applying the filters yourself). With a database per tenant
the filter and write checks are a second line of defence: a connection string that points at the wrong
database then shows no rows and rejects writes instead of mixing tenants.

### Pooling with a database per tenant

Do not resolve the connection string in an `AddDbContextPool` or `AddPooledDbContextFactory` callback. It
runs once, and EF Core keeps a pooled context's connection string between leases, so every pooled context
would keep the first tenant's database. Use `AddTenantDbContextPool` instead, and configure the provider
without a connection string:

```csharp
builder.Services.AddTenantDbContextPool<AppDbContext, string>((sp, options) =>
    options.UseSqlServer().AddTenantInterceptors(sp));
```

- It registers a scoped `AppDbContext` and `IDbContextFactory<AppDbContext>` that lease from one pool, and
  connects every lease to the current tenant's database. Use it instead of `AddDbContext`,
  `AddDbContextPool` or `AddPooledDbContextFactory` for that context.
- Leasing without a current tenant throws `TenantNotResolvedException`.
- Before a pooled context opens a connection, and again before every command it runs, a guard checks that
  the connection was set for this lease and belongs to the tenant that is current now. A context leased some
  other way, kept and used after switching to another tenant, or whose connection or connection string your
  code replaced, throws `TenantIsolationViolationException` instead of touching the wrong database. That
  includes a context whose connection is still open, whether you opened it or a transaction did.
- The guard cannot see SQL you run yourself on `Database.GetDbConnection()`. Nor does it stop a query that
  started before the tenant changed: a streaming or split query keeps reading from the database it started
  on, and those rows belong to the tenant that was current when it started. Use SQLite in-memory databases
  as tenant databases only in tests, because deleting one runs no command the guard can check.
- The context needs a constructor that takes only its options, as for any pooled context.
- The scoped context resolves the connection string synchronously, so it needs `GetConnectionString`.
  With only `GetConnectionStringAsync`, create contexts with `IDbContextFactory<T>.CreateDbContextAsync()`.

It is tested on SQLite, SQL Server, PostgreSQL and MySQL with one pooled instance serving two tenant
databases in turn, with concurrent leases, and with a context used as another tenant after its connection or
transaction was opened: saving, querying, raw SQL, bulk updates and deletes, creating, migrating and deleting
the database, and, on SQL Server and PostgreSQL, drawing HiLo keys.

The runnable [`DatabasePerTenant` sample](../samples/Tenantry.Samples.DatabasePerTenant) gives each tenant
its own SQLite file.

## Tested providers

Tenantry uses only standard EF Core features, but write isolation relies on each provider reporting the
rows an `UPDATE`/`DELETE` *matched* (the stored-tenant predicate turns a forged write into a zero-row
update that EF Core reports as a concurrency failure). The combinations below run the write-isolation
suite against a real database: forged updates and deletes, entities loaded under another tenant,
unchanged-value updates, writes without a tenant, tenant-filtered `ExecuteUpdate`/`ExecuteDelete`, the
`TenantId` bulk-update guard, pooled contexts, and pooled contexts with a database per tenant.

| Database | EF Core provider | Framework | Status |
|----------|------------------|-----------|--------|
| SQLite (in-memory) | `Microsoft.EntityFrameworkCore.Sqlite` | .NET 8, 9, 10 | Tested (unit suite) |
| SQL Server 2022 | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 | .NET 10 | Tested |
| PostgreSQL 16 | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | .NET 10 | Tested |
| MySQL 8.4 | `MySql.EntityFrameworkCore` (Oracle) 10.0.9 | .NET 10 | Tested |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | — | Not tested (no EF Core 10 release) |

Real-database runs currently cover .NET 10 only. If you use a MySQL connector option that reports
*changed* rather than *matched* rows (for example `UseAffectedRows=true`), an update that changes no
values reports zero rows and EF Core raises a false concurrency failure; keep the default.

## What is and isn't isolated

Tenantry isolates tenants in the application, through EF Core's query pipeline and `SaveChanges`. It is
**not** database-enforced row-level security: anything that reaches the database outside those paths is
not tenant-checked. If you need the database itself to enforce isolation (for example against direct SQL
access), add row-level security policies in the database as well, or use a database per tenant.

| Operation | Isolated? | Behaviour |
|-----------|-----------|-----------|
| LINQ queries | Yes | The query filter limits results to the current tenant; with no tenant they match nothing. |
| `SaveChanges` insert, update, delete | Yes | Inserts are stamped; updates and deletes must belong to the current tenant, checked in memory and in the SQL `WHERE` clause. Without a tenant, `OnMissingTenant` applies. |
| `ExecuteUpdate`, `ExecuteDelete` | Yes | The query filter limits affected rows to the current tenant; with no tenant they affect nothing. `ExecuteUpdate` may not set `TenantId`: when the query is compiled, a guard resolves each setter the way EF Core does (member access or `EF.Property`, through casts and through `Select`, `Join` and `SelectMany` projections) and throws `TenantIsolationViolationException` if it lands on `TenantId`. It fails closed on a setter it cannot resolve, such as one through a `GroupBy` projection or an `EF.Property` name it cannot read, and on setters it cannot read at all, as a new EF Core version could bring. It does not see a second property mapped to the `TenantId` column. |
| `IgnoreQueryFilters()` | No, by design | Removes the tenant filter from that query, including `ExecuteUpdate`/`ExecuteDelete`, which then affect **every** tenant. Treat it as a privileged operation. |
| Raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`) | No | Neither the filter nor the interceptors see raw SQL. Add the tenant predicate yourself. |
| Pooled contexts | Yes | Each use reads the tenant active at that moment; see [DbContext pooling](#dbcontext-pooling). |
| Other `DbContext` instances | No | Contexts created without `AddTenantInterceptors` (or not deriving from `MultiTenantDbContext`) get no write isolation, and nothing checks that their model has the tenant filters. With the interceptors, a model missing a filter or the `TenantId` concurrency token throws on its first query or save. |

## Migrations

The tenant filter and the `TenantId` concurrency token are part of the model, so they participate in
migrations normally (neither changes the schema):

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

A couple of notes:

- The `TenantId` column comes from your entity (via `ITenantEntity<TKey>`/`TenantEntity<TKey>`). Add
  the indexes your queries need (see above); `ApplyTenantFilters` does not create any.
- Design-time tooling (`dotnet ef`) constructs your `DbContext` without a real tenant. That is fine —
  the filter's fail-closed guard simply means design-time has "no tenant", which does not affect schema
  generation.

The [`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) uses real migrations, a database-backed
tenant store, mixed tenanted/global entities, cross-boundary relationships, and an admin endpoint.

## Non-HTTP usage

In console apps, workers, and background jobs there is no middleware to make a tenant current. Register with
`AddTenantry`, attach the interceptor exactly as above, and open a scope around each unit of work
with `ITenantScopeFactory<TKey>`, which also gives each tenant its own `DbContext`. The runnable [`EfCoreConsole` sample](../samples/Tenantry.Samples.EfCoreConsole) shows
stamping, read filtering, nested tenants, a rejected cross-tenant write, and fail-closed reads. See
[Non-HTTP hosts](non-http-hosts.md).
