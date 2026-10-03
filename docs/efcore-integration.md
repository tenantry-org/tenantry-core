# EF Core integration

`Tenantry.EfCore` isolates tenants' data in EF Core. `options.UseTenantry()` turns on both parts:

- Read isolation: a global query filter limits queries on `ITenantEntity<TKey>` entities to the current tenant.
- Write isolation: a `SaveChanges` interceptor stamps `TenantId` on new rows and rejects writes to other tenants' rows
  and, by default, writes with no tenant. Every `UPDATE` and `DELETE` also matches the stored tenant, and bulk updates
  cannot set `TenantId`.

It works on any `DbContext`, pooled or not, with no base class or interface, using only standard EF Core features
([tested providers](#tested-providers)). It reads the same `ITenantContext<TKey>` as the rest of Tenantry, so HTTP and
non-HTTP hosts behave the same.

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

`UseTenantry()` goes wherever the context's options are built (`AddDbContext`, `AddDbContextPool`,
`AddDbContextFactory`, `AddPooledDbContextFactory`, `OnConfiguring`). The context reads the tenant, and never sets it,
through `ITenantContext<TKey>` from its application service provider, which those registrations supply. So call
`AddTenantry` there with your entities' key type. Otherwise building the model throws, naming the call to add, and so
does every query and save (EF Core may share a model built for another application in the process).

## Read isolation: the global query filter

After your `OnModelCreating`, `UseTenantry()` gives every entity type that implements `ITenantEntity<TKey>` a global
query filter that matches the current tenant's rows, and makes `TenantId` a concurrency token, so updates and deletes
also match the stored tenant. A plain `db.Orders.ToListAsync()` returns only the current tenant's rows. Other entities
stay global.

Tenantry adds no indexes. Index `TenantId` yourself, usually as the leading column of composite indexes that match
your queries rather than alone:

```csharp
modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.CreatedAt });
modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.Reference }).IsUnique(); // unique per tenant
```

Derived types are covered by their root's filter. Owned types are read and checked through their owner: a save that
adds, moves, changes or deletes an owned entity needs its owner loaded or attached as the current tenant, and Tenantry
has the database confirm the owner's tenant. Details: [Advanced](#advanced-owned-and-multi-table-entities). Some
models cannot be isolated at all: see [the list](#models-that-cannot-be-isolated).

### Fail-closed behaviour

With no tenant current, the filter matches nothing, so reads return no rows. It checks whether a tenant is current
rather than comparing the id with a default value, and no tenant can have the default id.

### How the query filter stays correct

EF Core compiles a query filter once and caches it for every instance of the model, so a filter that captured a
tenant id or an `ITenantContext<TKey>` would keep the tenant current when it was compiled. Tenantry's filter reads the
tenant through the `DbContext` running the query, which EF Core evaluates as a parameter on every execution, so one
model serves every tenant and a pooled context serves whichever tenant is current when it queries.

Use a context for one tenant, as a request or an `ITenantScopeFactory` scope gives you. After a tenant switch on the
same context, `Find` and `Local` can still return entities loaded for the previous tenant (writing them is rejected).
A pooled context is reset between leases.

### Combining with your own query filters

The tenant filter is added after your `OnModelCreating` and ANDed with your own filter on the entity, so your
configuration can go anywhere, including `IEntityTypeConfiguration` classes. Before EF Core 10, the tenant filter is
merged into the entity's filter. On EF Core 10+, it is a named filter, `TenantryQueryFilters.Tenant`. EF Core does not
allow a named filter beside an unnamed one, so Tenantry names an unnamed filter of yours
`TenantryQueryFilters.Application`; both still apply, and each can be ignored alone. You can name your own:

```csharp
modelBuilder.Entity<Order>().HasQueryFilter("SoftDelete", o => !o.IsDeleted);
```

### Bypassing the filter (admin / reporting)

`IgnoreQueryFilters()` reads across tenants, for admin dashboards, cross-tenant reports or maintenance:

```csharp
var perTenant = await db.Orders
    .IgnoreQueryFilters()
    .GroupBy(o => o.TenantId)
    .Select(g => new { Tenant = g.Key, Count = g.Count() })
    .ToListAsync();
```

On EF Core 10, `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` removes only the tenant filter and keeps your other
named filters; `IgnoreQueryFilters()` removes them all. Either applies to that query only. Protect endpoints that use
it with authorization.

## Write isolation: the interceptor

The `SaveChanges`/`SaveChangesAsync` interceptor runs on every save of a context that uses `UseTenantry()`. For
entities that implement `ITenantEntity<TKey>`:

- **Added** entities get `TenantId` from the current tenant when it is unset (the key type's default, `null` or
  `string.Empty`). One that already names another tenant is rejected with `TenantIsolationViolationException`, not
  moved. The stamp goes through EF Core, so `TenantId` may have a private or init-only setter.
- **Modified** and **Deleted** entities must have been loaded or attached as the current tenant and still belong to
  it. Otherwise the interceptor throws `TenantIsolationViolationException` before anything is written, and the whole
  save is aborted. With a tenant current, this check always runs. Without one,
  [`OnMissingTenant`](#onmissingtenant-writes-with-no-tenant) decides.

The database checks too. `TenantId` is a concurrency token, so every `UPDATE` and `DELETE` includes
`AND TenantId = <tenant the entity was loaded or attached with>`. A detached entity that pairs another tenant's key
with the current tenant's `TenantId` passes the in-memory check but matches no row, so EF Core throws
`DbUpdateConcurrencyException` and nothing changes. The interceptor logs a warning when a tenant-scoped write matches
no row. No schema change is needed; your next migration's snapshot records the concurrency token. Entities mapped to
more than one table get extra checks: see [Advanced](#entities-mapped-to-more-than-one-table).

Call `UseTenantry()` after adding your own `SaveChanges` interceptors. An interceptor that runs after Tenantry's and
changes what a save writes, such as a soft delete that turns a delete into an update, is not checked.
`AddDbContextPerTenantDatabase` applies `UseTenantry()` before your configuration, so interceptors you add there run
after Tenantry's: they see new entities already stamped, and their changes are not checked.

`TenantIsolationViolationException` (namespace `Tenantry.EfCore`) says which check failed in `Kind`:

| `Kind` | Thrown when | `TypeName` | Tenant ids |
|--------|-------------|------------|------------|
| `EntityWrite` | `SaveChanges` would write another tenant's entity | the entity | the entity's and the current tenant |
| `BulkUpdate` | `ExecuteUpdate` would set `TenantId`, or sets a property the guard cannot identify | the entity | `null` |
| `TenantDatabaseMismatch` | A context would use another tenant's database ([database per tenant](#database-per-tenant)) | the `DbContext` | the database's and the current tenant (`null` when none) |
| `ModelConfiguration` | The model does not isolate a tenant-scoped entity type ([list](#models-that-cannot-be-isolated)) | the entity | `null` |
| `SaveWithoutTransaction` | An [all-or-nothing save](#saves-that-succeed-or-fail-as-a-whole) would run without a transaction, and `OnSaveWithoutTransaction` is `Reject` | the `DbContext` | `null` |
| `TransactionRolledBack` | A transaction would commit or complete after an [all-or-nothing save](#saves-that-succeed-or-fail-as-a-whole) in it failed and EF Core could not undo it | the entity whose check failed, or the `DbContext` | `null` |

`OffendingTenantId` and `ExpectedTenantId` are strings for logging. When it is thrown, nothing has been written; a
refused commit's transaction is rolled back, so nothing is kept.

## Configuring write isolation

The defaults are the strictest settings, so this is optional:

```csharp
using Tenantry.EfCore;

builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigureEfCoreIsolation(options =>
    {
        options.OnMissingTenant = MissingTenantBehavior.Reject; // the default
        options.OnSaveWithoutTransaction = SaveWithoutTransactionBehavior.UseTransaction; // the default
    }));
```

`OnSaveWithoutTransaction` applies to an all-or-nothing save when `Database.AutoTransactionBehavior` is `Never`: run
it in a transaction EF Core begins (`UseTransaction`, the default), or throw before anything is sent (`Reject`). See
[Saves that succeed or fail as a whole](#saves-that-succeed-or-fail-as-a-whole).

### `OnMissingTenant`: writes with no tenant

The policy applies only to saves that write `ITenantEntity<TKey>` entities. Saves of host-level data only (the tenant
registry, a global catalogue, seeded reference data) never need a tenant.

| Value | Behaviour when tenant-scoped entities are saved with no tenant |
|-------|----------------------------------------|
| `Reject` *(default)* | Throws `TenantNotResolvedException` before anything is persisted. |
| `Warn` | The save proceeds and a structured warning is logged. |
| `Allow` | The save proceeds silently. |

`Warn` and `Allow` are for maintenance code that writes across tenants on purpose. Updates and deletes are then not
tenant-checked, and a new entity must set `TenantId` itself: a row with no tenant is always rejected. Reads with no
tenant still match nothing.

`ConfigureEfCoreIsolation` sets the policy for every context, so keep it at `Reject`. To relax it for one context kept
for maintenance code, use `UseTenantry(configure)`, which starts from the application's options:

```csharp
builder.Services.AddDbContext<MaintenanceDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Allow));
```

Or run maintenance one tenant at a time with `ITenantScopeFactory`.

## DbContext pooling

Pooled contexts need nothing extra: the filter and the interceptor read the tenant current when the context queries
or saves. As for any pooled context, it needs a single constructor that takes only its options.

```csharp
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(connectionString).UseTenantry());

// or, for IDbContextFactory<AppDbContext>
builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString).UseTenantry());
```

With a database per tenant, the connection must change with the tenant too: see below.

## Database per tenant

To give each tenant its own database, or route tenants to different servers, tell Tenantry how to find a tenant's
connection string and register the context with `AddDbContextPerTenantDatabase`. Configure the provider without a
connection string; each context connects to the current tenant's database.

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant-Id")
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(options =>
        options.GetConnectionString = t => $"Server=db;Database=app_{t.TenantId};Integrated Security=true")
    .AddDbContextPerTenantDatabase<AppDbContext>((sp, options) => options.UseSqlServer(), pooled: true));
```

- It registers a scoped `AppDbContext` and an `IDbContextFactory<AppDbContext>`; use it instead of `AddDbContext`,
  `AddDbContextPool` or `AddPooledDbContextFactory`. Call `UseConnectionStrings` first, or it throws. It returns the
  builder without its key type, so put it last (see [Registration](core-concepts.md#registration)).
- It applies `UseTenantry()` before your configuration (see [Write isolation](#write-isolation-the-interceptor)).
- `pooled: true` pools contexts as `AddDbContextPool` does, so the context needs a constructor that takes only its
  options. Unpooled, it can take other services and has its scope as its application service provider.
- Creating a context with no current tenant throws `TenantNotResolvedException` (for `dotnet ef`, see
  [Migrations](#migrations)).
- With only `GetConnectionStringAsync`, the scoped context reads its connection string when it first opens a
  connection. It can still be injected, but only asynchronous calls (`ToListAsync`, `SaveChangesAsync`) work on it;
  a synchronous one throws `InvalidOperationException`.

The tenant filter and write checks still apply, so a connection string that points at the wrong database shows no rows
and rejects writes. A guard also checks, before a context opens a connection and before every command, that the
connection was set for this context (and, pooled, this lease) and belongs to the current tenant. A context used after
a switch to another tenant, or whose connection or connection string your code replaced, throws
`TenantIsolationViolationException`, even if its connection is already open.

The guard cannot see SQL you run on `Database.GetDbConnection()`. A streaming or split query that started before the
tenant changed keeps reading from its database, whose rows belong to the tenant current when it started. Use SQLite
in-memory tenant databases only in tests: deleting one runs no command the guard can check.

Do not read the connection string yourself in an `AddDbContextPool` or `AddPooledDbContextFactory` callback: it runs
once, and EF Core keeps a pooled context's connection string between leases, so every lease would use the first
tenant's database.

`UseConnectionStrings` also registers `ITenantConnectionStringProvider<TKey>`, which returns a given tenant's
connection string (`Get(tenant)`, `GetAsync(tenant)`) for code that visits tenants without making each one current,
and `CurrentTenantConnectionString<TKey>`, which returns the current tenant's (`Get()`, `GetAsync()`) for code that
opens its own connections, and throws `TenantNotResolvedException` without one. The default provider calls your
delegate every time and does not cache.

For a secrets store, set `GetConnectionStringAsync`, which `GetAsync` prefers. To use a client from DI, register your
own provider, built from the application's services, and return `false` from its `CanGetSynchronously` if it can only
read asynchronously. To cache or log, wrap whichever provider is registered:

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(sp => new VaultConnectionStrings(sp.GetRequiredService<SecretClient>()))
    .DecorateConnectionStrings((sp, inner) => new LoggingConnectionStrings(inner)));
```

Tests on SQLite, SQL Server, PostgreSQL and MySQL cover one pool serving two tenant databases in turn, concurrent
leases, and a context used as another tenant after opening its connection or transaction, for saves, queries, raw SQL,
bulk updates and deletes, creating, migrating and deleting the database, and HiLo keys (SQL Server, PostgreSQL). The
[`DatabasePerTenant` sample](../samples/Tenantry.Samples.DatabasePerTenant) gives each tenant its own SQLite file.

## Extending: contributors

Packages that build on Tenantry (Tenantry Pro, or your own) can add to every context that uses `UseTenantry()`.
Register implementations as singletons:

- `ITenantDbContextOptionsContributor.Configure(DbContextOptionsBuilder)` runs inside `UseTenantry()`, for example to
  add an interceptor. `optionsBuilder.Options.ContextType` says which context.
- `ITenantModelContributor.Configure(ModelBuilder, DbContext)` runs while EF Core builds the model, after
  `OnModelCreating` and before the tenant filters, so entity types it adds are isolated too.

Both come from the context's application service provider; a context built without one runs none. Pooling,
`AddDbContextFactory` and `AddDbContextPerTenantDatabase` build a context's options only once, so an options
contributor must never depend on the current tenant.

`UseTenantry()` installs its own `IModelCustomizer`, so creating a context that also replaces it throws: move that
configuration into `OnModelCreating` or a model contributor. So does creating one with `UseInternalServiceProvider`,
as EF Core adds no extension's services to a provider you build. Compiled models (`dotnet ef dbcontext optimize`) are
not supported: EF Core compiles no model with query filters.

Two more seams let a package fail closed:

- `TenantContextGuard` is an interceptor that calls your `Check(DbContext)` before the context opens a connection,
  runs a command or saves. Throw `TenantNotResolvedException` or `TenantIsolationViolationException` from it.
  Tenantry's database-per-tenant guard is one.
- `TenantModel` says which entity types a model isolates: `HasTenantOwnedEntityTypes`, `IsTenantOwned`, and
  `FindUnisolatedEntityTypes`, the types neither tenant-owned nor marked as shared by every tenant. Mark such a type
  with `[SharedAcrossTenants]` or `modelBuilder.Entity<Country>().IsSharedAcrossTenants()`. The marker changes nothing
  in queries or saves; marking a tenant-owned type fails the model check.

## What is and isn't isolated

Tenantry isolates tenants in the application, through EF Core's queries and `SaveChanges`. It is not row-level
security in the database: anything that reaches the database another way is not checked. If the database itself must
enforce isolation (against direct SQL access, say), add row-level security there too, or use a database per tenant.

| Operation | Isolated? | Behaviour |
|-----------|-----------|-----------|
| LINQ queries | Yes | Limited to the current tenant; with no tenant they match nothing. |
| `SaveChanges` insert, update, delete | Yes | Inserts are stamped; updates and deletes must belong to the current tenant, checked in memory and in the SQL `WHERE` clause. Without a tenant, `OnMissingTenant` applies. |
| `ExecuteUpdate`, `ExecuteDelete` | Yes | Limited to the current tenant's rows; with no tenant they affect nothing. When the query is compiled, a guard resolves each `ExecuteUpdate` setter as EF Core does (member access or `EF.Property`, through casts and `Select`, `Join` and `SelectMany` projections) and throws if one sets `TenantId`. It also throws on a setter it cannot resolve (through `GroupBy`, or an `EF.Property` name it cannot read) or read at all, as a new EF Core version could bring. It misses a second property mapped to the `TenantId` column. |
| `IgnoreQueryFilters()` | No, by design | Removes the tenant filter from that query, so `ExecuteUpdate`/`ExecuteDelete` then affect every tenant. |
| Raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`) | No | Neither the filter nor the interceptors see it. Add the tenant predicate yourself. |
| `Entry(…).Reload()`, `GetDatabaseValues()` | Yes | EF Core reads the row by key without query filters, but Tenantry keeps the tenant filter, so another tenant's row reads as deleted: `GetDatabaseValues()` returns `null` and `Reload()` detaches the entity. On EF Core 10 the entity's other filters are still ignored (an unnamed one is named `TenantryQueryFilters.Application`); on EF Core 8 and 9 its own filter applies too, merged with the tenant filter. |
| Entities a context already tracks | No | `Find` and `Local` answer from the change tracker, which keeps entities loaded for an earlier tenant. Use a context for one tenant. |
| Pooled contexts | Yes | Each use reads the tenant current at that moment. |
| Other `DbContext` instances | No | A context whose options do not call `UseTenantry()` gets no isolation. |

### Models that cannot be isolated

Building these models throws `TenantIsolationViolationException` (or `InvalidOperationException` for the registration):

- a tenant-scoped type whose base entity type is not tenant-scoped;
- a tenant-scoped owned type whose owner is not tenant-scoped;
- an owned type with no `TenantId`, under a tenant-scoped owner, whose key does not include its owner's key
  (`OwnsMany(…, b => b.HasKey(x => x.Id))`): an update or delete by that key could reach another tenant's row. Keep EF
  Core's default key, or implement `ITenantEntity<TKey>` on it;
- an owned type owned by a tenant-scoped type through a key that neither includes nor is part of the owner's primary
  key, nor includes its `TenantId` (`WithOwner().HasPrincipalKey(o => o.Code)`): Tenantry checks the owner by its
  primary key, which need not be the row the owned rows name;
- a tenant-scoped owned type mapped to JSON (`ToJson()`): it lives in its owner's row, under the owner's `TenantId`,
  and EF Core cannot check a `TenantId` of its own (EF Core 10 rejects the concurrency token itself), so do not
  implement `ITenantEntity<TKey>` on it;
- a type that is not tenant-scoped mapped to a tenant-scoped entity's table (table splitting): with no filter or
  `TenantId`, it would read and change every tenant's rows there. This fails on the first query or save, as only the
  finished model says which tables a type is mapped to;
- entities that implement `ITenantEntity<TKey>` with more than one key type;
- entities whose key type Tenantry is not registered for (`AddTenantry<Guid>` with `ITenantEntity<string>`);
- a tenant-scoped entity whose `TenantId` is not a mapped public property of the key type, such as one implemented
  explicitly (`Guid ITenantEntity<Guid>.TenantId => OrganizationId`);
- on EF Core 10, a filter of your own named `TenantryQueryFilters.Tenant`, which the tenant filter would replace.

Something that runs after `UseTenantry()`, such as a model-building convention, can still remove the tenant filter or
concurrency token. The interceptors check each model on its first query and first save, and throw
`TenantIsolationViolationException` instead of running either if a tenant-scoped entity type has lost one.

## Migrations

The tenant filter and the `TenantId` concurrency token are part of the model and take part in migrations as usual.
Neither changes the schema:

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

- The `TenantId` column comes from your entity (`ITenantEntity<TKey>` or `TenantEntity<TKey>`). Tenantry creates no
  indexes.
- `dotnet ef` builds the model with no tenant current, which does not affect the schema. A design-time factory
  (`IDesignTimeDbContextFactory`) that builds options without the application's services can still call
  `UseTenantry()`; only querying and saving need them. If a package adds to the model through an
  `ITenantModelContributor`, which runs only with those services, build them in the factory and pass them with
  `UseApplicationServiceProvider`.
- A context registered with `AddDbContextPerTenantDatabase` needs such a factory, as it cannot be created without a
  current tenant.

The [`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) uses real migrations, a database-backed tenant store,
tenanted and global entities, relationships between them, and an admin endpoint.

## Non-HTTP usage

Console apps, workers and background jobs have no middleware to make a tenant current. Use `AddTenantry` and
`UseTenantry()` as above, and open a scope around each unit of work with `ITenantScopeFactory<TKey>`, which also gives
each tenant its own `DbContext`. The [`EfCoreConsole` sample](../samples/Tenantry.Samples.EfCoreConsole) shows
stamping, read filtering, nested tenants, a rejected cross-tenant write, and fail-closed reads. See
[Non-HTTP hosts](non-http-hosts.md).

## Tested providers

Write isolation relies on the provider reporting the rows an `UPDATE` or `DELETE` matched: a forged write matches no
row, which EF Core reports as a concurrency failure. These combinations run the write-isolation suite (forged writes,
entities loaded under another tenant, unchanged-value updates, writes without a tenant, `ExecuteUpdate`/`ExecuteDelete`
and the `TenantId` guard, `GetDatabaseValues` of another tenant's row, pooled contexts, and a database per tenant)
against a real database:

| Database | EF Core provider | Framework | Status |
|----------|------------------|-----------|--------|
| SQLite (in-memory) | `Microsoft.EntityFrameworkCore.Sqlite` | .NET 8, 9, 10 | Tested (unit suite) |
| SQL Server 2022 | `Microsoft.EntityFrameworkCore.SqlServer` 8.0.31, 9.0.20, 10.0.12 | .NET 8, 9, 10 | Tested |
| PostgreSQL 16 | `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.4, 9.0.0, 10.0.3 | .NET 8, 9, 10 | Tested |
| MySQL 8.4 | `Pomelo.EntityFrameworkCore.MySql` 8.0.2, 9.0.0 | .NET 8, 9 | Tested |
| MySQL 8.4 | `MySql.EntityFrameworkCore` (Oracle) 10.0.9 | .NET 10 | Tested |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | .NET 10 | Not tested (no EF Core 10 release) |

Each framework runs the suite with its own EF Core version. MariaDB is not tested. Keep MySQL's default of reporting
matched rows: with an option that reports changed rows (such as `UseAffectedRows=true`), an update that changes no
values reports zero rows and EF Core raises a false concurrency failure.

## Advanced: owned and multi-table entities

Owned rows in a table of their own, and the rows of an entity mapped to more than one table, have no tenant check of
their own. Tenantry checks them through another statement and keeps the save all-or-nothing.

### Owned entities

EF Core reads owned rows only through their owner and allows them no filter of their own, so an owned type is isolated
through its owner whether or not it implements `ITenantEntity<TKey>`. A tenant-scoped owned type's `TenantId` is still
a concurrency token.

Writes are checked through the nearest tenant-scoped owner. A save that adds an owned entity, moves one with its own
key to another owner (by changing its foreign key), or changes or deletes one with no `TenantId` of its own needs that
owner loaded or attached as the current tenant. The database then confirms the owner's tenant:

- An owner the save does not otherwise write (loaded or only attached, or modified with nothing EF Core writes) has
  its `TenantId` written back with its concurrency token, so an audit log sees an update of the owner.
- An owner deleted and added again under the same key, which EF Core saves as one `UPDATE` of what differs, has its
  stored row read.
- An owner whose `TenantId` is part of the key its owned types are owned through needs neither: their foreign key
  names the tenant.
- An owner whose `TenantId` EF Core does not write after an insert (it is part of another key, such as an alternate key
  on `(TenantId, Id)`, or is configured not to be saved) has its stored row read before the save, one query per owner.
  The read names the current tenant and ignores every query filter, yours too, as EF Core's own writes do, so an owner
  your filter hides (an archived one, say) can still be given owned entities.

An owned entity saved without its owner in the same context is rejected. With no tenant, `OnMissingTenant` treats
owned entities as tenant-scoped. Owned rows in their own table rely on the owner's statement, so the save must succeed
or fail as a whole (below).

### Entities mapped to more than one table

An entity mapped to more than one table (table-per-type inheritance, entity splitting) is updated only in the tables
whose columns changed. So when one changes, its `TenantId` is also written back to its table to be checked there, or,
when EF Core does not save `TenantId`, its stored row is read before the save. One keyed by its `TenantId` needs
neither: every table's key names the tenant. A save that deletes such an entity and adds one under the same key, which
EF Core saves as an `UPDATE` of what differs, table by table, has the deleted one's stored row read. Rows outside the
table with `TenantId` rely on that table's statement, so the save must succeed or fail as a whole.

### Saves that succeed or fail as a whole

Rows checked through another statement are safe only if the whole save is undone when the check fails. EF Core
usually ensures this with a transaction, but not in every setup. For these saves:

- The failed check cannot be suppressed. An interceptor of yours that suppresses concurrency failures
  (`ThrowingConcurrencyException`), such as "last write wins" or EF Core's sample that ignores rows already deleted,
  still works for other entities, but Tenantry throws a failed check that other rows depend on, wherever yours is
  registered. Interceptors added after `UseTenantry()`, including those packages add through it, do not see it.
- With `Database.AutoTransactionBehavior` set to `Never`, EF Core still runs the save in a transaction of its own, and
  the setting goes back to `Never` when the save ends (event 2005, at `Debug`). Other saves still run without one. If
  your database or connection pooler cannot run transactions, set `OnSaveWithoutTransaction` to `Reject`: such a save
  then throws `TenantIsolationViolationException` of kind `SaveWithoutTransaction` before anything is sent.
  - Hand a transaction you began through ADO.NET to EF Core with `Database.UseTransaction`, or EF Core cannot begin
    its own and the save fails.
  - If a `SavingChanges` interceptor registered after Tenantry's stops the save, the setting stays `WhenNeeded` until
    the context's next save sets it back to `Never`.
- In your own transaction, EF Core rolls a failed save back to a savepoint it creates first, and Tenantry turns
  savepoints on for the save if you turned them off (`AutoSavepointsEnabled = false`). A transaction without
  savepoints, such as SQL Server with multiple active result sets (MARS), is rolled back instead of committed if such a
  save in it failed after sending any statement, or if EF Core could not roll back to its savepoint. `Commit` then
  throws `TenantIsolationViolationException` of kind `TransactionRolledBack` (event 2004).
  - Any failure of such a save counts. A save can fail before EF Core reads the check (on a duplicate key, say), so
    Tenantry cannot know whether the check held, and a forged write looks like a real conflict.
  - So does a save Tenantry never learns succeeded, as when an interceptor added before `UseTenantry()` throws from
    `SavedChanges`.
- In a `TransactionScope`, or a transaction the connection was enlisted in, EF Core creates no savepoint. The same
  failures roll the transaction back when it completes, so disposing the completed scope throws
  `TransactionAbortedException`.

Not covered:

- EF Core's in-memory provider, which has no transactions;
- SQLite, or another provider that cannot join a `TransactionScope`, used inside one with EF Core's
  `AmbientTransactionWarning` turned off: it then saves with no transaction at all;
- storage without transactions, such as MySQL's MyISAM tables;
- an interceptor that suppresses EF Core's savepoint commands;
- a transaction handed to EF Core with `UseTransaction` and then committed directly through ADO.NET.
