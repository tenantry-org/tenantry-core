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

> Resolving the tenant from a header without authentication lets any caller select any tenant. In production,
> authenticate callers and check that they belong to the tenant they select ([Access control](access-control.md)).

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
`AddDbContextFactory`, `AddPooledDbContextFactory`, `OnConfiguring`). It reads the current tenant from
`ITenantContext<TKey>` in the context's application service provider, so register Tenantry in that provider with
`AddTenantry<TKey>`, using your entities' key type. Without it, building the model throws an exception naming the call
to add, and so does every query and save.

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
has the database confirm the owner's tenant. Details: [Advanced](efcore-advanced.md). Some
models cannot be isolated at all: see [the list](efcore-advanced.md#models-that-cannot-be-isolated).

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

- Added entities get `TenantId` from the current tenant when it is unset (the key type's default, `null` or
  `string.Empty`). One that already names another tenant is rejected with `TenantIsolationViolationException`, not
  moved. The stamp goes through EF Core, so `TenantId` may have a private or init-only setter.
- Modified and deleted entities must have been loaded or attached as the current tenant and still belong to
  it. Otherwise the interceptor throws `TenantIsolationViolationException` before anything is written, and the whole
  save is aborted. With a tenant current, this check always runs. Without one,
  [`OnMissingTenant`](#onmissingtenant-writes-with-no-tenant) decides.

The database checks too. `TenantId` is a concurrency token, so every `UPDATE` and `DELETE` includes
`AND TenantId = <tenant the entity was loaded or attached with>`. A detached entity that pairs another tenant's key
with the current tenant's `TenantId` passes the in-memory check but matches no row, so EF Core throws
`DbUpdateConcurrencyException` and nothing changes. The interceptor logs a warning when a tenant-owned write matches
no row. No schema change is needed; your next migration's snapshot records the concurrency token. Entities mapped to
more than one table get extra checks: see [Advanced](efcore-advanced.md#entities-mapped-to-more-than-one-table).

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
| `ModelConfiguration` | The model does not isolate a tenant-owned entity type ([list](efcore-advanced.md#models-that-cannot-be-isolated)), or has [entity types that are not tenant-owned](#entity-types-that-are-not-tenant-owned) or shared | the entity, or the `DbContext` | `null` |
| `SaveWithoutTransaction` | An [all-or-nothing save](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole) would run without a transaction, and `OnSaveWithoutTransaction` is `Reject` | the `DbContext` | `null` |
| `TransactionRolledBack` | A transaction would commit or complete after an [all-or-nothing save](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole) in it failed and EF Core could not undo it | the entity whose check failed, or the `DbContext` | `null` |
| `TenantSchemaMismatch` | A context would use another tenant's schema. Thrown by packages that put tenants in schemas of their own, such as Tenantry.Pro, from a [`TenantContextGuard`](#extending-contributors) | the `DbContext` | as the package sets them |

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
        options.OnUnclassifiedEntityType = UnclassifiedEntityTypeBehavior.Reject; // the default
    }));
```

`OnUnclassifiedEntityType` is described under
[Entity types that are not tenant-owned](#entity-types-that-are-not-tenant-owned).

`OnSaveWithoutTransaction` applies to an all-or-nothing save when `Database.AutoTransactionBehavior` is `Never`: run
it in a transaction EF Core begins (`UseTransaction`, the default), or throw before anything is sent (`Reject`). See
[Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole).

### `OnMissingTenant`: writes with no tenant

The policy applies only to saves that write `ITenantEntity<TKey>` entities. Saves of host-level data only (the tenant
registry, a global catalogue, seeded reference data) never need a tenant.

| Value | Behaviour when tenant-owned entities are saved with no tenant |
|-------|----------------------------------------|
| `Reject` (default) | Throws `TenantNotResolvedException` before anything is persisted. |
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
  `AddDbContextPool` or `AddPooledDbContextFactory`. Call `UseConnectionStrings` first, or it throws. The first context
  created throws when an `ITenantConnectionStringProvider<TKey>` of your own is registered as scoped or transient: the
  factory is a singleton, so the provider must be one. Like `UseResolver<T>()`, it returns the builder without its key
  type ([Registration](core-concepts.md#registration)).
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
read asynchronously. Use the delegates or a provider, not both: `UseConnectionStrings` throws when they are combined.
To cache or log, wrap whichever provider is registered:

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
- `TenantModel` says which entity types a model isolates: `HasTenantOwnedEntityTypes`, `IsTenantOwned`,
  `IsSharedAcrossTenants`, and `FindUnisolatedEntityTypes`, the types `UseTenantry()` refuses in a model with
  tenant-owned types ([Entity types that are not tenant-owned](#entity-types-that-are-not-tenant-owned)).

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
| `FromSql`, `FromSqlRaw`, `FromSqlInterpolated` on a tenant-owned entity | Yes | EF Core applies the entity's query filters over your SQL, so it returns only the current tenant's rows; with no tenant, none. |
| `Database.SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlRaw` | No | They map to no entity type, so no filter applies, and no interceptor sees what they change. Add the tenant predicate yourself. |
| `Entry(…).Reload()`, `GetDatabaseValues()` | Yes | EF Core reads the row by key without query filters, but Tenantry keeps the tenant filter, so another tenant's row reads as deleted: `GetDatabaseValues()` returns `null` and `Reload()` detaches the entity. On EF Core 10 the entity's other filters are still ignored (an unnamed one is named `TenantryQueryFilters.Application`); on EF Core 8 and 9 its own filter applies too, merged with the tenant filter. |
| Entities a context already tracks | No | `Find` and `Local` answer from the change tracker, which keeps entities loaded for an earlier tenant. Use a context for one tenant. |
| Pooled contexts | Yes | Each use reads the tenant current at that moment. |
| Other `DbContext` instances | No | A context whose options do not call `UseTenantry()` gets no isolation. |

Some models cannot be isolated, and building them throws: see
[Models that cannot be isolated](efcore-advanced.md#models-that-cannot-be-isolated).

### Entity types that are not tenant-owned

Tenantry isolates only tenant-owned entity types, those that implement `ITenantEntity<TKey>` and the owned types they
own. Every tenant reads and writes the rows of any other entity type. So in a model with at least one tenant-owned
type, `UseTenantry()` requires every other entity type to be marked as shared by every tenant, and the context's first
query or save throws `TenantIsolationViolationException` of kind `ModelConfiguration`, naming each type that is
neither. A model with no tenant-owned type, such as a database-per-tenant context's, is not checked.

Mark a type every tenant shares, such as a country list or the tenant registry, with the attribute or in
`OnModelCreating`:

```csharp
using Tenantry.EfCore;

[SharedAcrossTenants]
public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Currency
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

public class CatalogueDbContext(DbContextOptions<CatalogueDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>();
        modelBuilder.Entity<Country>();
        modelBuilder.Entity<Currency>().IsSharedAcrossTenants();
    }
}
```

The marker changes nothing in queries or saves, and marking a tenant-owned type fails the model check. A type follows
the type it belongs to: a derived type its hierarchy's root, an owned type its owner, and the join entity of a
many-to-many relationship the types it joins (when either is tenant-owned, it must be too). Keyless types, types
mapped to a view and shared-type entity types (`SharedTypeEntity`) need a marker like any other; EF Core's
migrations history table is not part of the model. For ASP.NET Core Identity's types, see
[ASP.NET Core Identity](aspnetcore-identity.md#the-user-type-and-context).

`OnUnclassifiedEntityType` decides what happens to such a model: `Reject` (the default) throws, `Warn` logs event 2006
and uses it, and `Allow` uses it silently. `Warn` logs once for each model EF Core builds, which is usually once per
context type; it logs again if EF Core drops the model from its cache and builds it again. Set the option for one
context with `UseTenantry(o => …)`. Contexts with different values of it never share a query EF Core has compiled, so
each is checked under its own.

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

The write-isolation suite runs against SQLite, SQL Server, PostgreSQL and MySQL on every supported .NET version. The
versions and caveats are in [Compatibility](compatibility.md#databases).

## Owned and multi-table entities

Owned entities, entities mapped to more than one table, and the saves that must succeed or fail as a whole are
described in [Owned and multi-table entities](efcore-advanced.md).
