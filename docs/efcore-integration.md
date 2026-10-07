# EF Core integration

`Tenantry.EfCore` isolates tenants' data in EF Core. `options.UseTenantry()` adds a global query filter that limits
queries on `ITenantEntity<TKey>` entities to the current tenant ([read
isolation](#read-isolation-the-global-query-filter)), and a `SaveChanges` interceptor that stamps and checks writes
([write isolation](#write-isolation-the-interceptor)). It works on any `DbContext`, pooled or not, with no base class or
interface, and uses only standard EF Core features ([tested providers](#tested-providers)). It reads the same
`ITenantContext<TKey>` as the rest of Tenantry, so HTTP and non-HTTP hosts behave the same.

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
`ITenantContext<TKey>` in the context's application service provider, so register Tenantry there with
`AddTenantry<TKey>`, using your entities' key type. Without it, building the model, and every query and save, throws
an exception naming the call to add. It does not work with a compiled model (`dotnet ef dbcontext optimize`),
`UseInternalServiceProvider`, or options that replace `IModelCustomizer`
([`UseTenantry`](api/microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md)).

## Read isolation: the global query filter

`db.Orders.ToListAsync()` returns only the current tenant's rows. With no tenant current, it returns none. Other
entities stay global.

After your `OnModelCreating`, `UseTenantry()` gives every `ITenantEntity<TKey>` entity type a global query filter on
the current tenant. It also makes `TenantId` a concurrency token, so updates and deletes match the stored tenant too.

Tenantry adds no indexes. Index `TenantId` yourself, usually as the leading column of composite indexes that match
your queries rather than alone:

```csharp
modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.CreatedAt });
modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.Reference }).IsUnique(); // unique per tenant
```

Derived types are covered by their root's filter. Owned types are read and checked through their owner
([Owned entities](efcore-advanced.md#owned-entities)). Some models cannot be isolated at all
([the list](efcore-advanced.md#models-that-cannot-be-isolated)). How one compiled filter serves every tenant is under
[Details](#how-the-query-filter-stays-correct).

### String tenant ids and the database's collation

Give each tenant a `string` id the database cannot confuse with another's. The query filter and the `WHERE` clause of
updates and deletes compare `TenantId` in the database, under the column's collation. Under a collation that ignores
case, `acme` and `ACME` are one tenant to the database: each one's queries return the other's rows, and its updates and
deletes can change them. Tenantry compares ids exactly, so it cannot see this.

SQL Server's and MySQL's default collations ignore case, and MySQL's also ignores accents. SQL Server ignores trailing
spaces whatever the collation. PostgreSQL compares exactly by default, but not on a `citext` column or under a
nondeterministic collation.

`Guid` or `int` keys avoid the question. So does a binary collation on `TenantId` (`UseCollation`, or
`ForMySQLHasCollation` with Oracle's MySQL provider, which does not apply `UseCollation`), except that SQL Server still
ignores trailing spaces. A store that reads tenants from a table keyed by the id, under the same collation, keeps ids
apart: the key refuses a second id the collation takes for the first. `UseInMemoryStore` refuses two `string` ids that
differ only in case; other collisions (accents, trailing spaces, `ß` and `ss` under some collations) are yours to
avoid. A store of your own over configuration or another service guarantees nothing.

On SQL Server and MySQL, warning 2007 (`StringTenantIdCollation`) names the tables a model leaves to the default
collation, when it builds the model ([Diagnostics](diagnostics.md#logs)). A custom key type stored as text is not
checked. If your ids cannot collide, turn it off with [`IgnoreWarnings`](diagnostics.md#turning-off-a-warning).

### Combining with your own query filters

Your own query filters still apply. The tenant filter is added after your `OnModelCreating` and ANDed with your filter
on the entity, so your configuration can go anywhere, including `IEntityTypeConfiguration` classes.

On EF Core 8 and 9 the tenant filter is merged into the entity's filter. On EF Core 10 and later it is a named filter,
`TenantryQueryFilters.Tenant`. EF Core does not allow a named filter beside an unnamed one, so Tenantry names an
unnamed filter of yours `TenantryQueryFilters.Application`. Both still apply, and each can be ignored alone. You can
name your own:

```csharp
modelBuilder.Entity<Order>().HasQueryFilter("SoftDelete", o => !o.IsDeleted);
```

### Bypassing the filter (admin / reporting)

`IgnoreQueryFilters()` reads across tenants, for admin dashboards, reports or maintenance. Protect endpoints that use
it with authorization.

```csharp
var perTenant = await db.Orders
    .IgnoreQueryFilters()
    .GroupBy(o => o.TenantId)
    .Select(g => new { Tenant = g.Key, Count = g.Count() })
    .ToListAsync();
```

On EF Core 10, `IgnoreQueryFilters([TenantryQueryFilters.Tenant])` removes only the tenant filter and keeps your other
named filters; `IgnoreQueryFilters()` removes them all. Either applies to the whole query, including the tenant-owned
entities a shared entity's query includes, joins or reads through a navigation. [TNY1002](analyzers.md#tny1002) warns
of a call whose query, in the same expression, reads a tenant-owned entity. Where crossing tenants is meant, as here,
suppress the warning there with the reason.

## Write isolation: the interceptor

The interceptor runs on every `SaveChanges`/`SaveChangesAsync` of a context that uses `UseTenantry()`. For
`ITenantEntity<TKey>` entities:

- Added entities get `TenantId` from the current tenant when it is unset (the key type's default, `null` or
  `string.Empty`). One that already names another tenant is rejected with `TenantIsolationViolationException`, not
  moved. The stamp goes through EF Core, so `TenantId` may have a private or init-only setter.
- Modified and deleted entities must have been loaded or attached as the current tenant and still belong to it.
  Otherwise the interceptor throws `TenantIsolationViolationException` before anything is written, and the whole save is
  aborted. With a tenant current this check always runs. Without one,
  [`OnMissingTenant`](#onmissingtenant-writes-with-no-tenant) decides.

The database checks too. As `TenantId` is a concurrency token, every `UPDATE` and `DELETE` includes
`AND TenantId = <tenant the entity was loaded or attached with>`. A detached entity that pairs another tenant's key with
the current tenant's `TenantId` passes the in-memory check but matches no row. EF Core then throws
`DbUpdateConcurrencyException`, and nothing changes. The interceptor logs warning 2003 for any tenant-owned update or
delete that matches no row. No schema change is needed; your next migration's snapshot records the concurrency token.
Entities mapped to more than one table get extra checks
([Advanced](efcore-advanced.md#entities-mapped-to-more-than-one-table)).

Call `UseTenantry()` after adding your own `SaveChanges` interceptors. One that runs after Tenantry's and changes what
a save writes, such as a soft delete that turns a delete into an update, is not checked. `AddDbContextPerTenantDatabase`
applies `UseTenantry()` first ([Database per tenant](#database-per-tenant)).

`TenantIsolationViolationException` (namespace `Tenantry.EfCore`) says which check failed in `Kind`, and which entity
or context it concerns in `TypeName`. `OffendingTenantId` and `ExpectedTenantId` are strings for logging. When it is
thrown nothing has been written, and a refused commit's transaction is rolled back.
[`TenantIsolationViolationKind`](api/tenantry-efcore-tenantisolationviolationkind.md) lists the checks, and
[`TenantIsolationViolationException`](api/tenantry-efcore-tenantisolationviolationexception.md) what each property holds
for each.

## Configuring write isolation

Both defaults are the strictest settings, so this is optional:

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

A third option, `OnUnmarkedEntityType`, is off by default: see
[Entity types that are not tenant-owned](#entity-types-that-are-not-tenant-owned).

`OnSaveWithoutTransaction` applies to an all-or-nothing save when `Database.AutoTransactionBehavior` is `Never`.
`UseTransaction`, the default, runs it in a transaction EF Core begins. `Reject` throws
`TenantIsolationViolationException` of kind `SaveWithoutTransaction` before anything is sent
([Saves that succeed or fail as a whole](efcore-advanced.md#saves-that-succeed-or-fail-as-a-whole)).

### `OnMissingTenant`: writes with no tenant

The policy applies only to saves that write `ITenantEntity<TKey>` entities, their owned entities or their many-to-many
join rows. Saves of host-level data only (the tenant registry, a global catalogue, seeded reference data) never need a
tenant.

| Value | Behaviour when tenant-owned entities are saved with no tenant |
|-------|----------------------------------------|
| `Reject` (default) | Throws `TenantNotResolvedException` before anything is persisted. |
| `Warn` | The save proceeds and a structured warning is logged. |
| `Allow` | The save proceeds silently. |

`Warn` and `Allow` are for maintenance code that writes across tenants on purpose. Updates and deletes are then not
tenant-checked, and join rows are written unchecked. A new entity must set `TenantId` itself: a row with no tenant is
always rejected. Reads with no tenant still match nothing.

`ConfigureEfCoreIsolation` sets the policy for every context. Keep it at `Reject`, and relax it for one maintenance
context with `UseTenantry(configure)`, which starts from the application's options:

```csharp
builder.Services.AddDbContext<MaintenanceDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry(o => o.OnMissingTenant = MissingTenantBehavior.Allow));
```

Or run maintenance one tenant at a time with `ITenantScopeFactory`.

## DbContext pooling

Pooled contexts need nothing extra: the filter and the interceptor read the tenant current when the context queries
or saves. Like any pooled context, it needs a single constructor that takes only its options.

```csharp
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(connectionString).UseTenantry());

// or, for IDbContextFactory<AppDbContext>
builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString).UseTenantry());
```

With a database per tenant, the connection must change with the tenant too.

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

- It registers a scoped `AppDbContext` and an `IDbContextFactory<AppDbContext>`. Use it instead of `AddDbContext`,
  `AddDbContextPool` or `AddPooledDbContextFactory` ([why](#why-database-per-tenant-has-its-own-registration)).
- Call `UseConnectionStrings` first, or it throws. An `ITenantConnectionStringProvider<TKey>` of your own must be a
  singleton, as the factory is one.
- It applies `UseTenantry()` before your configuration, so interceptors you add there run after Tenantry's. They see
  new entities already stamped, and their changes are not checked.
- `pooled: true` pools contexts as `AddDbContextPool` does, so the context needs a constructor that takes only its
  options. Unpooled, it can take other services.
- Creating a context with no current tenant throws `TenantNotResolvedException` (for `dotnet ef`, see
  [Migrations](#migrations)).
- With only `GetConnectionStringAsync`, the scoped context can still be injected, but only asynchronous calls
  (`ToListAsync`, `SaveChangesAsync`) work on it.

Like `UseResolver<T>()`, it returns the builder without its key type ([Registration](core-concepts.md#registration)).
Its [API reference](api/microsoft-extensions-dependencyinjection-tenantryefcoretenantbuilderextensions.md) says what
each of these rules throws, and when.

The tenant filter and write checks still apply, so a connection string pointing at the wrong database shows no rows and
rejects writes. A guard also checks the connection before a context opens it and before every command. The connection
must have been set for this context (and, pooled, this lease) and belong to the current tenant. A context used after a
switch to another tenant throws `TenantIsolationViolationException`, even with its connection already open. So does one
whose connection or connection string your code replaced. Use SQLite in-memory tenant databases only in tests. The
[API reference](api/microsoft-extensions-dependencyinjection-tenantryefcoretenantbuilderextensions.md) lists what the
guard cannot see: SQL you run on `Database.GetDbConnection()`, and a streaming or split query started before the tenant
changed.

Do not read the connection string yourself in an options callback EF Core runs once, such as a pool's or a factory's:
every tenant would use the first tenant's database. Use `AddDbContextPerTenantDatabase`
([`CurrentTenantConnectionString`](api/tenantry-currenttenantconnectionstring.md) names the registrations).

`UseConnectionStrings` also registers two services for code of your own.
[`ITenantConnectionStringProvider<TKey>`](api/tenantry-itenantconnectionstringprovider.md) returns a given tenant's
connection string, for code that visits tenants without making each one current.
[`CurrentTenantConnectionString<TKey>`](api/tenantry-currenttenantconnectionstring.md) returns the current tenant's, for
code that opens its own connections. The default provider calls your delegate every time and does not cache.

For a secrets store, set `GetConnectionStringAsync`, which `GetAsync` prefers. To use a client from DI, register your
own provider, built from the application's services. If it can only read asynchronously, return `false` from its
`CanGetSynchronously`. Use the delegates or a provider, not both: `UseConnectionStrings` throws on the two together. To
cache or log, wrap whichever provider is registered:

```csharp
builder.Services.AddTenantry<string>(tenant => tenant
    .UseStore<AppTenantStore>()
    .UseConnectionStrings(sp => new VaultConnectionStrings(sp.GetRequiredService<SecretClient>()))
    .DecorateConnectionStrings((sp, inner) => new LoggingConnectionStrings(inner)));
```

The [`DatabasePerTenant` sample](../samples/Tenantry.Samples.DatabasePerTenant) gives each tenant its own SQLite file.

### Creating and migrating tenant databases

`dotnet ef database update` cannot apply your migrations to the tenant databases. It updates one database, and a
context registered with `AddDbContextPerTenantDatabase` cannot be created without a current tenant. Apply them from
the application instead, once for each tenant, for example as a step of each deployment before the new version serves
requests:

```csharp
using Microsoft.EntityFrameworkCore;

// Inject ITenantLookup<TKey> tenants and ITenantScopeFactory<TKey> scopes.
foreach (var tenant in await tenants.GetAllTenantsAsync(cancellationToken))
{
    await using var scope = scopes.CreateScope(tenant);
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync(cancellationToken);
}
```

The loop leaves four things to you:

- Creating databases only on purpose. `MigrateAsync` creates a database that does not exist, if the connection may
  create databases. A tenant whose connection string is wrong, or whose database was dropped while it stays in the
  store, then gets a new, empty database that its requests reach. Create each tenant's database when you add the
  tenant, and skip and report one whose database is missing (`Database.CanConnectAsync`).
- Keeping suspended tenants in the store, so that it migrates them too. `CreateScope` does not check whether a tenant
  is active ([Non-HTTP hosts](non-http-hosts.md#when-you-already-hold-the-tenant)). A tenant that misses a migration
  breaks when it is reactivated ([Suspended and inactive tenants](tenant-stores.md#suspended-and-inactive-tenants)).
- Going on after a failure. As written, one tenant's failure ends the loop before the tenants after it. Catch each
  tenant's failure, go on with the rest, and fail the deployment at the end.
- Running it once. Two instances running it at the same time both migrate every database, and can race on the same
  one. Run it from one place, such as a deployment job, not as every instance starts.

Tenantry.Pro runs this as a deployment step (`migrate-tenants`) with per-tenant reports, concurrency and failure
limits, and creates the database when a tenant is provisioned
([Tenant migrations](https://tenantry.dev/docs/pro/migration-orchestration)).

### A second database per tenant

`UseConnectionStrings` gives each tenant one connection string, so every context `AddDbContextPerTenantDatabase`
registers connects to the same database. For a second per-tenant database, such as a reporting one, register its
context with `AddDbContext`. Build the connection string from the current tenant in the options callback, which
`AddDbContext` runs for each scope:

```csharp
builder.Services.AddDbContext<ReportingDbContext>((sp, options) =>
{
    var tenant = sp.GetRequiredService<ITenantContext<string>>().RequiredTenant;
    options.UseSqlServer($"Server=reports;Database=reports_{tenant.TenantId};Integrated Security=true").UseTenantry();
});

public class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options);
```

That context is not pooled, since a pool runs the options callback once. It has no guard: used after a switch to
another tenant, it keeps the first tenant's database. To fail closed there too, pass the tenant id to a guard derived
from `TenantContextGuard` ([Extending](#extending-contributors)) whose `Check` throws unless that tenant is current.
Add it in the same callback with `AddInterceptors`. `dotnet ef` needs an `IDesignTimeDbContextFactory` for such a
context, as creating it with no tenant throws.

## What is and isn't isolated

Tenantry isolates tenants in the application, through EF Core's queries and `SaveChanges`. It is not row-level
security: anything that reaches the database another way is not checked. If the database itself must enforce
isolation, for example against direct SQL access, add row-level security there too, or use a database per
tenant.

| Operation | Isolated? | Behaviour |
|-----------|-----------|-----------|
| LINQ queries | Yes | Limited to the current tenant; with no tenant they match nothing. |
| `SaveChanges` insert, update, delete | Yes | Inserts are stamped; updates and deletes must belong to the current tenant, checked in memory and in the SQL `WHERE` clause. Without a tenant, `OnMissingTenant` applies. |
| `ExecuteUpdate`, `ExecuteDelete` | Yes | Limited to the current tenant's rows; with no tenant they affect nothing. An `ExecuteUpdate` that sets `TenantId` throws ([Details](#executeupdate-and-executedelete)). |
| Many-to-many join rows | Yes | Read through both ends' query filters. A save that adds, changes or deletes one confirms each tenant-owned end it names ([Many-to-many relationships](efcore-advanced.md#many-to-many-relationships)). |
| `IgnoreQueryFilters()` | No, by design | Removes the tenant filter from that query, so `ExecuteUpdate`/`ExecuteDelete` then affect every tenant. |
| `FromSql`, `FromSqlRaw`, `FromSqlInterpolated` on a tenant-owned entity | Yes | EF Core applies the entity's query filters over your SQL, so it returns only the current tenant's rows; with no tenant, none. SQL that EF Core cannot wrap to add the filter, such as a stored procedure call, throws `InvalidOperationException`. |
| `Database.SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlRaw` | No | They map to no entity type, so no filter applies, and no interceptor sees what they change. Add the tenant predicate yourself. |
| Third-party bulk libraries (EFCore.BulkExtensions, Entity Framework Extensions, linq2db) | No | Tenantry stamps and checks writes only in `SaveChanges` and limits only `ExecuteUpdate` and `ExecuteDelete`, so inserts, updates and deletes these libraries run another way are neither stamped nor checked. Use `SaveChanges`, `ExecuteUpdate` or `ExecuteDelete` instead. |
| `Entry(…).Reload()`, `GetDatabaseValues()` | Yes | Another tenant's row reads as deleted: `GetDatabaseValues()` returns `null` and `Reload()` detaches the entity ([Details](#reloading-an-entity)). |
| Entities a context already tracks | No | `Find` and `Local` answer from the change tracker, which keeps entities loaded for an earlier tenant; writing them is rejected. Use a context for one tenant, as a request or an `ITenantScopeFactory` scope gives you. |
| Pooled contexts | Yes | Each use reads the tenant current at that moment. A pooled context is reset between leases. |
| Other `DbContext` instances | No | A context whose options do not call `UseTenantry()` gets no isolation. |

### Entity types that are not tenant-owned

An entity type that does not implement `ITenantEntity<TKey>`, and is not owned by one that does, is shared by every
tenant. Queries read all its rows, and saves write them without a tenant check. That is by design, for reference data,
a product catalogue or the tenant registry. The one exception is Tenantry.Pro's mixed mode, where a `Shared` tenant's
context with an unmarked type is refused ([Mixed mode](https://tenantry.dev/docs/pro/mixed-mode#shared-tenants)).

To catch a type left without `ITenantEntity<TKey>` by mistake, mark the shared types, with the attribute or in
`OnModelCreating`. Then set `OnUnmarkedEntityType` so that `UseTenantry()` checks the rest:

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

builder.Services.AddTenantry<Guid>(tenant => tenant
    .UseStore<EfCoreTenantStore>()
    .ConfigureEfCoreIsolation(options => options.OnUnmarkedEntityType = UnmarkedEntityTypeBehavior.Reject));
```

- `Allow`, the default, checks nothing.
- `Warn` logs warning 2006 and uses the model ([Diagnostics](diagnostics.md#logs) says how often).
- `Reject` makes the context's first query or save throw `TenantIsolationViolationException` of kind
  `ModelConfiguration`, naming each unmarked type.

Only a model with at least one tenant-owned type is checked, so a database-per-tenant context is not. Set the option
for one context with `UseTenantry(o => …)`.

The marker changes nothing in queries or saves, and marking a tenant-owned type fails the model check. A type follows
the type it belongs to: a derived type its hierarchy's root, and an owned type its owner. The join entity of a
many-to-many relationship follows the types it joins, unless it has properties or foreign keys beyond the two it joins
by.
Keyless types, types mapped to a view and shared-type entity types (`SharedTypeEntity`) need a marker like any other;
EF Core's migrations history table is not part of the model.

In an application that sets `Warn` or `Reject`, options that call `UseTenantry()` before `UseApplicationServiceProvider`
throw on their first query, save or command
([`UseTenantry`](api/microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md)). `AddDbContext` and
its relatives set the services first. A static compiled
query (`EF.CompileQuery`) needs one copy per value
([`UnmarkedEntityTypeBehavior`](api/tenantry-efcore-unmarkedentitytypebehavior.md)).

Without the option, a test can list the unmarked types (xUnit here):

```csharp no-compile
[Fact]
public void EveryEntityTypeIsTenantOwnedOrMarkedShared()
{
    using var db = CreateContext();
    Assert.Empty(TenantModel.FindUnisolatedEntityTypes(db.Model));
}
```

## Migrations

The tenant filter and the `TenantId` concurrency token are part of the model and take part in migrations as usual;
neither changes the schema:

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

- The `TenantId` column comes from your entity (`ITenantEntity<TKey>` or `TenantEntity<TKey>`). Tenantry creates no
  indexes.
- `dotnet ef` builds the model with no tenant current, which does not affect the schema.
- A design-time factory (`IDesignTimeDbContextFactory`) that builds options without the application's services can
  still call `UseTenantry()`; only querying and saving need them. A package that adds to the model through an
  `ITenantModelContributor` needs them too: build them in the factory and pass them with
  `UseApplicationServiceProvider`.
- A context registered with `AddDbContextPerTenantDatabase` needs such a factory, as it cannot be created without a
  current tenant. To apply its migrations to every tenant's database, see
  [Creating and migrating tenant databases](#creating-and-migrating-tenant-databases).

The [`EfCoreWeb` sample](../samples/Tenantry.Samples.EfCoreWeb) uses real migrations, a database-backed tenant store,
tenanted and global entities, relationships between them, and an admin endpoint.

## Non-HTTP usage

Console apps, workers and background jobs open a scope around each unit of work with `ITenantScopeFactory<TKey>`,
which also gives each tenant its own `DbContext`: see [Non-HTTP hosts](non-http-hosts.md) and the
[`EfCoreConsole` sample](../samples/Tenantry.Samples.EfCoreConsole).

## Tested providers

The write-isolation suite runs against SQLite, SQL Server, PostgreSQL and MySQL on every supported .NET version. The
versions and caveats are in [Compatibility](compatibility.md#databases).

For a database per tenant, tests on all four cover one pool serving two tenant databases in turn, and concurrent
leases. They also cover a context used as another tenant after opening its connection or transaction: for saves,
queries, raw SQL, bulk updates and deletes, creating, migrating and deleting the database, and HiLo keys (SQL Server,
PostgreSQL).

## Details

### How the query filter stays correct

EF Core compiles a query filter once and reuses it for every context with that model. A filter that captured a tenant
id or an `ITenantContext<TKey>` would keep the tenant current when it was compiled. Tenantry's filter reads the tenant
through the `DbContext` running the query, which EF Core evaluates as a parameter on every execution. So one model
serves every tenant, and a pooled context serves whichever tenant is current when it queries.

The filter fails closed. It checks whether a tenant is current rather than comparing the id with a default value, and
no tenant can have the default id.

### ExecuteUpdate and ExecuteDelete

An `ExecuteUpdate` that sets `TenantId`, or has a setter Tenantry cannot resolve, throws when the query is compiled.
[`UseTenantry`](api/microsoft-entityframeworkcore-tenantrydbcontextoptionsbuilderextensions.md) says which setters the
guard resolves, and what it misses. On a many-to-many join entity's set, which has no tenant filter, both affect every
tenant's join rows ([Many-to-many relationships](efcore-advanced.md#many-to-many-relationships)).

### Reloading an entity

EF Core reads the row by key without query filters, but Tenantry keeps the tenant filter. On EF Core 10 the entity's
other filters are still ignored (an unnamed one is named `TenantryQueryFilters.Application`). On EF Core 8 and 9 its
own filter applies too, merged with the tenant filter.

### Why database per tenant has its own registration

Tenantry sets each context's connection string when the context, or a pooled context's lease, is handed out. So the
connection is the current tenant's before anything can use it, including code that calls `Database.GetDbConnection()`.
EF Core has no hook for the start of a pooled lease: under `AddDbContextPool`, a pooled context would keep the previous
tenant's connection string until EF Core first opened a connection. With only `GetConnectionStringAsync`, the previous
one is cleared at hand-out, and the tenant's is read when the context first opens a connection.

### Extending: contributors

Packages that build on Tenantry (Tenantry.Pro, or your own) can add to every context that uses `UseTenantry()`. They
register these as singletons, resolved from the context's application service provider; a context built without one
runs none:

- [`ITenantDbContextOptionsContributor`](api/tenantry-efcore-itenantdbcontextoptionscontributor.md) runs inside
  `UseTenantry()`, for example to add an interceptor. Pooling, `AddDbContextFactory` and
  `AddDbContextPerTenantDatabase` build a context's options only once, so it must never depend on the current tenant.
- [`ITenantModelContributor`](api/tenantry-efcore-itenantmodelcontributor.md) runs while EF Core builds the model,
  after `OnModelCreating` and before the tenant filters, so entity types it adds are isolated too.

Two more seams let a package fail closed:

- [`TenantContextGuard`](api/tenantry-efcore-tenantcontextguard.md) is an interceptor that calls your
  `Check(DbContext)` before the context opens a connection, runs a command or saves. Throw `TenantNotResolvedException`
  or `TenantIsolationViolationException` from it. Tenantry's database-per-tenant guard is one.
- [`TenantModel`](api/tenantry-efcore-tenantmodel.md) says which entity types a model isolates. Its
  `FindUnisolatedEntityTypes` returns the types `OnUnmarkedEntityType = Reject` refuses in a model with tenant-owned
  types.
