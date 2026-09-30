# EF Core integration

`Tenantry.EfCore` provides the data isolation that makes multi-tenancy real. It has two independent
halves:

- **Read isolation** — a global query filter restricts every query against an `ITenantEntity<TKey>`
  entity to the current tenant.
- **Write isolation** — a `SaveChanges` interceptor stamps `TenantId` on new rows and rejects writes of
  another tenant's rows before saving; the stored tenant is also part of every `UPDATE` and `DELETE`
  statement, and bulk updates cannot change `TenantId`. Tenant-scoped writes with no tenant are rejected by
  default.

One call on a context's options turns on both: `options.UseTenantry()`. It works on **any** `DbContext`,
pooled or not, with no base class and no interface, using only standard EF Core features, so it is
provider-agnostic; see [tested providers](#tested-providers) for what the test suite covers. It reads the
same `ITenantContext<TKey>` used everywhere else, so HTTP and non-HTTP hosts behave identically.

## Setup at a glance

> **Introductory setup.** Resolving the tenant from a header without authentication lets any caller
> select any tenant. Use it to learn the API. For production, authenticate callers and validate that
> they belong to the tenant they select, as in the [`SecureApi` sample](../samples/Tenantry.Samples.SecureApi).

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;
using Tenantry.EfCore;

// 1. Register Tenantry
builder.Services.AddTenantry<Guid>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseInMemoryStore(tenants));

// 2. Isolate the DbContext
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());
```

```csharp
// 3. Mark entities tenant-owned
public class Order : TenantEntity<Guid>
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

// 4. The DbContext stays a plain DbContext
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Your own configuration, in any order, including your own query filters.
    }
}
```

`UseTenantry()` goes wherever the context's options are built: `AddDbContext`, `AddDbContextPool`,
`AddDbContextFactory`, `AddPooledDbContextFactory`, or `OnConfiguring`. It reads the current tenant through the
context's application service provider, which those registrations supply, so Tenantry must be registered there
with `AddTenantry` for the key type your entities use. If it is not, building the model throws, naming the call
to add, and so does every query and save (EF Core may share a model built for another application in the process). The context itself only ever *reads* the tenant, through `ITenantContext<TKey>` (never
`ITenantContextSetter<TKey>`).

## Read isolation: the global query filter

While EF Core builds the model, after your `OnModelCreating`, `UseTenantry()` goes through every entity type that
implements `ITenantEntity<TKey>`:

- it adds a global query filter that matches the current tenant's rows, and nothing when no tenant is current,
  and
- it marks `TenantId` as a concurrency token, so updates and deletes also match on the stored tenant.

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
covers the derived types. EF Core reads an owned type's rows only through its owner and does not let it have a
filter of its own, so a tenant-scoped owned type is isolated through its owner; its `TenantId` is still a
concurrency token. When a save adds an owned entity to an owner that is only attached, not loaded or changed, the
owner's `TenantId` is written back with its concurrency token, so the database confirms the owner is the current
tenant's (an audit log sees an update of the owner).

These models cannot be isolated, so building them throws `TenantIsolationViolationException` (or, for the
registration, `InvalidOperationException`):

- a tenant-scoped type whose base entity type is not tenant-scoped;
- a tenant-scoped owned type whose owner is not tenant-scoped;
- entities that implement `ITenantEntity<TKey>` with more than one key type;
- entities whose key type Tenantry is not registered for (`AddTenantry<Guid>` with `ITenantEntity<string>`);
- a tenant-scoped entity whose `TenantId` is not a mapped public property of the key type, such as one implemented
  explicitly (`Guid ITenantEntity<Guid>.TenantId => OrganizationId`);
- on EF Core 10, a filter of your own named `TenantryQueryFilters.Tenant`, which the tenant filter would replace.

### Fail-closed behaviour

When **no tenant is current**, the filter matches **nothing**. Reads return zero rows rather than leaking every
tenant's data. This is deliberate: a missing tenant is treated as "see nothing", not "see everything". The
filter asks whether a tenant is current, rather than comparing the id with a default value, and Tenantry never
lets a tenant have the default id.

### How the query filter stays correct

EF Core compiles a global query filter **once** and caches the plan across all instances of the model.
If the filter captured an `ITenantContext<TKey>` instance, or the current tenant id, every query would use
whatever was current when the plan was first built — a serious leak.

Tenantry's filter reads the tenant through the `DbContext` that runs the query, and EF Core evaluates that
part again, as a query parameter, on **every** execution. So one model serves every tenant, a pooled context
serves whichever tenant is current when it queries, and a query with no tenant matches nothing.

Use a context for one tenant, as a request or an `ITenantScopeFactory` scope gives you. Its change tracker keeps
what it loaded, so after a tenant switch on the same context `Find` and `Local` can still return entities loaded for
the previous tenant (writing them is rejected). A pooled context is reset between leases.

### Combining with your own query filters

The tenant filter is added after your `OnModelCreating` and combined with any filter you configured on the
entity (with logical AND), so the order of your configuration does not matter: configure entity types and
filters wherever you like, including in `IEntityTypeConfiguration` classes.

On **EF Core 10+**, the tenant filter is a *named* filter, `TenantryQueryFilters.Tenant`, alongside your
named filters. EF Core does not allow a named filter beside an unnamed one, so when an entity has an unnamed
filter, the tenant filter is merged into it instead (and Tenantry logs this once, when it builds the model).
Before EF Core 10, filters have no names, and the tenant filter is always merged. Name your own filters on
EF Core 10 to keep them apart:

```csharp
modelBuilder.Entity<Order>().HasQueryFilter("SoftDelete", o => !o.IsDeleted);
```

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

On EF Core 10, `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` removes only the tenant filter and keeps your
other named filters. `IgnoreQueryFilters()` removes them all.

This bypasses the read filter only. Use it consciously and guard such endpoints with appropriate
authorization — it is the one place the isolation is intentionally off.

## Write isolation: the interceptor

The `SaveChanges`/`SaveChangesAsync` interceptor runs on every save of a context that uses `UseTenantry()`, and
for entities implementing `ITenantEntity<TKey>`:

- **Added** entities have their `TenantId` **stamped** from the current tenant when it is unset (the key
  type's default, `null` or `string.Empty`). An `Added` entity that already names **another** tenant is
  rejected with `TenantIsolationViolationException` rather than silently moved, which catches code (or a
  request body) trying to write into another tenant. The stamp goes through EF Core, so `TenantId` may have a
  private or init-only setter.
- **Modified / Deleted** entities are **validated**: the entity must have been loaded or attached as the
  current tenant and must still belong to it. Otherwise the interceptor throws
  `TenantIsolationViolationException` **before any data is written** and the whole `SaveChanges` is
  aborted. This is **always on**, regardless of configuration.
- **The database enforces ownership too.** `TenantId` is a concurrency token, so every `UPDATE` and `DELETE`
  includes `AND TenantId = <tenant the entity was loaded or attached with>`. A detached entity that pairs another
  tenant's primary key with the current tenant's `TenantId` passes the in-memory check but matches no row, so EF
  Core throws `DbUpdateConcurrencyException` and nothing is changed. The interceptor logs a warning when a
  tenant-scoped write matches no row. No schema change is needed; your next migration's model snapshot records the
  concurrency token.

If there is **no resolved tenant**, behaviour follows the `OnMissingTenant` policy (below).

`TenantIsolationViolationException` (namespace `Tenantry.EfCore`) says which check failed in `Kind`:

| `Kind` | Thrown when | `TypeName` | Tenant ids |
|--------|-------------|------------|------------|
| `EntityWrite` | `SaveChanges` would write another tenant's entity (above) | the entity | the entity's and the current tenant |
| `BulkUpdate` | An `ExecuteUpdate` would set `TenantId`, or sets a property the guard cannot identify | the entity | `null` |
| `TenantDatabaseMismatch` | A database-per-tenant context would use another tenant's database ([below](#database-per-tenant)) | the `DbContext` | the database's and the current tenant (`null` when none) |
| `ModelConfiguration` | A model does not isolate a tenant-scoped entity type ([above](#read-isolation-the-global-query-filter), and [below](#what-is-and-isnt-isolated)) | the entity | `null` |

`OffendingTenantId` and `ExpectedTenantId` are strings for logging. Nothing has been written when it is thrown.

## Configuring write isolation

The defaults are the strictest settings, so this is optional:

```csharp
using Tenantry.EfCore;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigureEfCoreIsolation(options => options.OnMissingTenant = MissingTenantBehavior.Reject)); // the default
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

Pooled contexts need nothing extra. A pooled instance is reused across requests, and because the filter and
the interceptor read the tenant that is current when the context queries or saves, each request sees only its
own tenant. As for any pooled context, it needs a single constructor that takes only its options.

```csharp
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(connectionString).UseTenantry());

// or, for IDbContextFactory<AppDbContext>
builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString).UseTenantry());
```

This covers shared-database isolation. With a database per tenant, the connection must change with the tenant;
see [Database per tenant](#database-per-tenant).

## Database per tenant

To give each tenant its own database (or route tenants to different servers), tell Tenantry how to find a
tenant's connection string, and register the context with `AddDbContextPerTenantDatabase`. Configure the
provider **without** a connection string: each context is connected to the current tenant's database.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true")
    .AddDbContextPerTenantDatabase<AppDbContext>((sp, options) => options.UseSqlServer(), pooled: true));
```

- It registers a scoped `AppDbContext` and an `IDbContextFactory<AppDbContext>`, and applies `UseTenantry()` before
  your configuration, so interceptors you add see new entities already stamped. Use it instead of `AddDbContext`,
  `AddDbContextPool` or `AddPooledDbContextFactory` for that context. Call `UseConnectionStrings` first; without it,
  `AddDbContextPerTenantDatabase` throws. It returns the builder without its key type, so it goes after the methods
  that need one.
- `pooled: true` reuses contexts from a pool, as `AddDbContextPool` does; the context then needs a constructor
  that takes only its options. Without a pool, a context can take other services in its constructor, and has its
  scope as its application service provider, as with `AddDbContext`.
- `dotnet ef` cannot create the context, because no tenant is current at design time: give it an
  `IDesignTimeDbContextFactory` (see [Migrations](#migrations)).
- Creating a context without a current tenant throws `TenantNotResolvedException`.
- The scoped context reads the connection string synchronously, so it needs `GetConnectionString`. With only
  `GetConnectionStringAsync`, create contexts with `IDbContextFactory<T>.CreateDbContextAsync()`.

The tenant filter and write checks still apply, as a second line of defence: a connection string that points at
the wrong database then shows no rows and rejects writes instead of mixing tenants. So does a guard: before a
context opens a connection, and again before every command it runs, it checks that the connection was set for
this context (and, pooled, for this lease) and belongs to the tenant that is current now. A context kept and used
after switching to another tenant, or whose connection or connection string your code replaced, throws
`TenantIsolationViolationException` instead of touching the wrong database. That includes a context whose
connection is still open, whether you opened it or a transaction did.

- The guard cannot see SQL you run yourself on `Database.GetDbConnection()`. Nor does it stop a query that
  started before the tenant changed: a streaming or split query keeps reading from the database it started
  on, and those rows belong to the tenant that was current when it started. Use SQLite in-memory databases
  as tenant databases only in tests, because deleting one runs no command the guard can check.

Do not read the connection string yourself in an `AddDbContextPool` or `AddPooledDbContextFactory` callback: it
runs once, and EF Core keeps a pooled context's connection string between leases, so every pooled context would
keep the first tenant's database.

`UseConnectionStrings` also registers `ITenantConnectionStringProvider<TKey>`, which returns a given tenant's
connection string (`Get(tenant)`, `GetAsync(tenant)`) for code that visits tenants without making each one
current (implement it to decorate the default provider, for example to cache), and
`CurrentTenantConnectionString<TKey>`, which returns the current tenant's (`Get()`, `GetAsync()`) for code that
opens its own connections, and throws `TenantNotResolvedException` without one. Set `GetConnectionStringAsync`
when the string comes from a secrets store: `GetAsync` prefers it, and the synchronous `Get` then needs
`GetConnectionString` as well. The default provider calls your delegate every time and does not cache.

It is tested on SQLite, SQL Server, PostgreSQL and MySQL with one pooled instance serving two tenant
databases in turn, with concurrent leases, and with a context used as another tenant after its connection or
transaction was opened: saving, querying, raw SQL, bulk updates and deletes, creating, migrating and deleting
the database, and, on SQL Server and PostgreSQL, drawing HiLo keys.

The runnable [`DatabasePerTenant` sample](../samples/Tenantry.Samples.DatabasePerTenant) gives each tenant
its own SQLite file.

## Extending: contributors

Packages that build on Tenantry (Tenantry Pro, or your own) can add to every context that uses `UseTenantry()`
without asking for another call on each one. Register implementations as singletons:

- `ITenantDbContextOptionsContributor.Configure(DbContextOptionsBuilder)` runs inside `UseTenantry()`, for
  example to add an interceptor. `optionsBuilder.Options.ContextType` says which context.
- `ITenantModelContributor.Configure(ModelBuilder, DbContext)` runs while EF Core builds the model, after
  `OnModelCreating` and before the tenant filters, so an entity type it adds is isolated too.

Both come from the context's application service provider, so a context built without it runs none. Several
registrations build a context's options only once (pooling, `AddDbContextFactory`, `AddDbContextPerTenantDatabase`),
so an options contributor must never depend on the current tenant.

`UseTenantry()` installs its own EF Core `IModelCustomizer`, so a context that uses it must not also replace
`IModelCustomizer` (creating it throws, rather than losing one or the other): move that configuration into
`OnModelCreating` or a model contributor. For the same reason it cannot be combined with `UseInternalServiceProvider`
(EF Core adds no extension's services to a provider you build), and creating such a context throws too. Compiled
models (`dotnet ef dbcontext optimize`) are not supported: EF Core compiles no model with query filters.

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
| `Entry(…).Reload()`, `GetDatabaseValues()` | No | EF Core reads the row by its key without query filters, so an entity attached with another tenant's key gets that tenant's values. A `DbUpdateConcurrencyException` handler must not return `GetDatabaseValues()` of a tenant-owned entity to the caller: a forged key reaches another tenant's row there (writing it back is still rejected). |
| Entities a context already tracks | No | `Find` and `Local` answer from the change tracker, which keeps entities loaded for an earlier tenant if the same context is used after a tenant switch. Use a context for one tenant. |
| Pooled contexts | Yes | Each use reads the tenant active at that moment; see [DbContext pooling](#dbcontext-pooling). |
| Other `DbContext` instances | No | A context whose options do not call `UseTenantry()` gets no isolation at all. |

`UseTenantry()` adds the tenant filter and concurrency token last, but a model can still lose them afterwards, for
example to a model-building convention. The interceptors check each model on its first query and its first save, and throw
`TenantIsolationViolationException` instead of running either if a tenant-scoped entity type has lost its tenant
filter or its `TenantId` concurrency token. To read across tenants on purpose, use `IgnoreQueryFilters()`.

## Migrations

The tenant filter and the `TenantId` concurrency token are part of the model, so they participate in
migrations normally (neither changes the schema):

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

A couple of notes:

- The `TenantId` column comes from your entity (via `ITenantEntity<TKey>`/`TenantEntity<TKey>`). Add
  the indexes your queries need (see above); Tenantry does not create any.
- Design-time tooling (`dotnet ef`) builds the model with no tenant current, which does not affect schema
  generation. A design-time factory (`IDesignTimeDbContextFactory`) that builds options by hand, without the
  application's services, can still call `UseTenantry()`, and only querying or saving needs the application's
  services. The model is the same unless a package adds to it through an `ITenantModelContributor`, which runs only
  with the application's services: then build them in the factory and pass them with `UseApplicationServiceProvider`.
- A context registered with `AddDbContextPerTenantDatabase` needs such a factory, because `dotnet ef` cannot create
  it without a current tenant.

The [`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) uses real migrations, a database-backed
tenant store, mixed tenanted/global entities, cross-boundary relationships, and an admin endpoint.

## Non-HTTP usage

In console apps, workers, and background jobs there is no middleware to make a tenant current. Register with
`AddTenantry`, use `UseTenantry()` exactly as above, and open a scope around each unit of work with
`ITenantScopeFactory<TKey>`, which also gives each tenant its own `DbContext`. The runnable
[`EfCoreConsole` sample](../samples/Tenantry.Samples.EfCoreConsole) shows stamping, read filtering, nested
tenants, a rejected cross-tenant write, and fail-closed reads. See [Non-HTTP hosts](non-http-hosts.md).
