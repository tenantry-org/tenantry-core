# Migrating from Finbuckle.MultiTenant

This guide moves an ASP.NET Core application with EF Core from Finbuckle.MultiTenant 10.1.4 to Tenantry. Work
through the numbered steps in order; the sections after them list the behaviour that changes and what Tenantry does
not have.

The `tenantry-migrate-from-finbuckle` skill in the [Tenantry agent
skills](https://github.com/tenantry-org/tenantry-agent-skills) takes a coding agent through these steps
([For AI coding agents](ai-agents.md) says how to install them).

## How the concepts map

| Finbuckle.MultiTenant | Tenantry |
|-----------------------|----------|
| `ITenantInfo`, `TenantInfo` (`Id`, `Identifier`, `Name`) | `ITenantDescriptor<TKey>`, `TenantDescriptor<TKey>` (`TenantId`, `Name`) |
| `AddMultiTenant<TTenantInfo>()` | `AddTenantry<TKey>(tenant => …)`, generic over the key type |
| Strategies: `WithHeaderStrategy()`, `WithHostStrategy()`, … | Resolvers: `ResolveFromHeader(…)`, `ResolveFromSubdomain()`, … |
| One or more stores: `WithInMemoryStore()`, `WithEFCoreStore<…>()`, … | One `ITenantStore<TKey>`: `UseInMemoryStore(…)` or `UseStore<TStore>()` |
| `UseMultiTenant()`, before `UseAuthentication()` | `UseTenantry()`, after `UseAuthentication()`; with per-tenant authentication, `UseTenantResolution()` before it too |
| `IMultiTenantContextAccessor<TTenantInfo>`, `HttpContext.GetMultiTenantContext<TTenantInfo>()` | `ITenantContext<TKey>` |
| `IMultiTenantContextSetter`, `HttpContext.SetTenantInfo(…)` | `ITenantContextSetter<TKey>.MakeCurrent(tenant)`, or `ITenantScopeFactory<TKey>` for a new DI scope |
| Finbuckle's base context class, or `IMultiTenantDbContext` with `EnforceMultiTenant()` | A plain `DbContext` registered with `options.UseTenantry()` |
| `[MultiTenant]`, `IsMultiTenant()` | `ITenantEntity<TKey>` or `TenantEntity<TKey>` on the entity |
| `IsNotMultiTenant()` | Nothing: an entity without `ITenantEntity<TKey>` is shared |
| `TenantMismatchMode` | No setting: a write to another tenant's row always throws |
| `TenantNotSetMode` | No setting: a new entity is stamped, a changed one without the tenant throws |
| `ConfigurePerTenant<TOptions, TTenantInfo>(…)`, its named and `ConfigureAllPerTenant` variants | `ConfigurePerTenant(perTenant => perTenant.Configure<TOptions>(…))`, with `Configure<TOptions>(name, …)` and `ConfigureAll<TOptions>(…)`, in `AddTenantry` (Tenantry.Options, [step 6](#6-move-per-tenant-options)) |
| `WithPerTenantAuthentication()` | `Configure<TOptions>(scheme, …)` in `ConfigurePerTenant`, with `UseTenantResolution()` ([Authentication per tenant](authentication-per-tenant.md)) |
| `MultiTenantIdentityDbContext` (Finbuckle.MultiTenant.Identity.EntityFrameworkCore) | `IdentityDbContext<TUser>` with a tenant-owned user type ([ASP.NET Core Identity](aspnetcore-identity.md)) |
| `ShortCircuitWhenTenantNotResolved()` | `RequireTenantByDefault()` |
| `ExcludeFromMultiTenantResolution()` | `AllowMissingTenant()` |
| `MultiTenantOptions.Events` | `ConfigureResolution(o => o.OnResolved = …)`, and `OnRejected` |

## 1. Swap the packages

```bash
dotnet remove package Finbuckle.MultiTenant.AspNetCore
dotnet remove package Finbuckle.MultiTenant.EntityFrameworkCore
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore
```

Remove any other Finbuckle package too, and every `using Finbuckle…` directive. Add `Tenantry.Options` if you configure
options per tenant. If you used `Finbuckle.MultiTenant.Identity.EntityFrameworkCore`, set Identity up as in
[ASP.NET Core Identity](aspnetcore-identity.md).

## 2. Keep string tenant ids

Finbuckle's tenant ids, and the `TenantId` column it adds to your tables, are strings. Register Tenantry with
`string` keys and that column and its data stay as they are.

To move to `Guid` or `int` keys, change the key type on your tenant type, your entities and `AddTenantry`, then add a
migration. The `AlterColumn` that EF Core generates for each `TenantId` column leaves converting the stored ids to the
database. Check that it does, or convert them yourself in the migration with `migrationBuilder.Sql`. Tenant ids stored
anywhere else, such as claims in issued tokens, change too.

## 3. Move the tenant type and store

As with Finbuckle's strategies, Tenantry's resolvers return an identifier, and the store's `FindByIdentifierAsync` finds
the tenant. By default it reads the identifier as the tenant id. When your identifiers differ from your ids, keep
`Identifier` on your own tenant type and look it up there:

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public class AppTenant : ITenantDescriptor<string>
{
    public string TenantId { get; set; } = "";     // Finbuckle's Id
    public string Identifier { get; set; } = "";   // what requests carry
    public string Name { get; set; } = "";
}

// The database that holds your tenant table, with AppTenant mapped to it.
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<AppTenant> Tenants => Set<AppTenant>();
}

public sealed class AppTenantStore(CatalogDbContext db) : ITenantStore<string>
{
    public async ValueTask<ITenantDescriptor<string>?> GetTenantAsync(string tenantId, CancellationToken ct = default) =>
        await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public async ValueTask<IReadOnlyList<ITenantDescriptor<string>>> GetAllTenantsAsync(CancellationToken ct = default) =>
        await db.Tenants.AsNoTracking().ToListAsync<ITenantDescriptor<string>>(ct);

    public async ValueTask<ITenantDescriptor<string>?> FindByIdentifierAsync(string identifier, CancellationToken ct = default) =>
        await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Identifier == identifier, ct);
}
```

This replaces Finbuckle's EF Core store. `UseInMemoryStore` finds tenants by id only, so it replaces Finbuckle's
in-memory store only where each identifier equals its id. Add `CacheTenants()` to avoid a database round trip on every
request ([Caching](tenant-stores.md#caching)).

## 4. Register Tenantry and its middleware

Finbuckle:

```csharp no-compile
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Finbuckle.MultiTenant.Extensions;

builder.Services.AddMultiTenant<AppTenantInfo>()
    .WithHeaderStrategy("X-Tenant")
    .WithEFCoreStore<TenantStoreDbContext, AppTenantInfo>()
    .ShortCircuitWhenTenantNotResolved();

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

var app = builder.Build();

app.UseMultiTenant();
app.UseAuthentication();
app.UseAuthorization();
```

Tenantry:

```csharp
builder.Services.AddDbContext<CatalogDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddTenantry<string>(tenant => tenant
    .ResolveFromHeader("X-Tenant")
    .UseStore<AppTenantStore>()
    .RequireTenantByDefault());

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTenantry();
```

Like Finbuckle's header strategy, `ResolveFromHeader` lets any caller name any tenant, so the build reports
[TNY2001](analyzers.md#tny2001) until you add an access validator
([Validating tenant access](access-control.md#validating-tenant-access)).

Finbuckle's claim strategy authenticated the request itself. `ResolveFromClaim` reads `HttpContext.User`, which the
authentication middleware sets, so `UseTenantry()` goes after `UseAuthentication()`
([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering) has the full rule).

If your authentication settings differ per tenant (Finbuckle's `WithPerTenantAuthentication()`,
[step 6](#6-move-per-tenant-options)), also call `UseTenantResolution()` before `UseAuthentication()`, where
`UseMultiTenant()` was. The tenant is then known when the scheme authenticates. Move `UseAuthorization()` after
`UseTenantry()`:

```csharp
app.UseTenantResolution();
app.UseAuthentication();
app.UseTenantry();
app.UseAuthorization();
```

`ShortCircuitWhenTenantNotResolved()` ended the request without an error status. `RequireTenantByDefault()` answers
`400` when the request names no tenant, `404` when it names an unknown one and `403` when the tenant is refused
([Status codes](aspnetcore-integration.md#status-codes)). To redirect instead, handle `OnRejected`
([Events](aspnetcore-integration.md#events)). Mark endpoints that work without a tenant, such as health checks, with
`AllowMissingTenant()`.

### Strategies and resolvers

| Finbuckle | Tenantry |
|-----------|----------|
| `WithHeaderStrategy()` | `ResolveFromHeader("__tenant__")` |
| `WithClaimStrategy()` | `ResolveFromClaim("__tenant__")` |
| `WithRouteStrategy()` | `ResolveFromRouteValue("__tenant__")` |
| `WithHostStrategy()` | `ResolveFromSubdomain(o => o.BaseDomains.Add("example.com"))` |
| `WithHostStrategy("__tenant__")` | `ResolveFromHost()` |
| `WithStrategy<T>()`, `WithDelegateStrategy(…)`, `WithHttpContextStrategy(…)` | `UseResolver<T>()`, with an `ITenantResolver` of your own |

Finbuckle's header, claim and route value are named `__tenant__` by default. Tenantry's header resolver needs a name;
its claim defaults to `tenant_id` and its route value to `tenant`. Pass the names your clients already send.

Finbuckle's default host template takes the first label of any host; without base domains, `ResolveFromSubdomain()`
takes it only from a host with at least three labels. List your domains in `BaseDomains`, `localhost` included
([Subdomain](tenant-resolution.md#subdomain)).

### Reading the tenant

Inject `ITenantContext<string>` where you injected `IMultiTenantContextAccessor<AppTenantInfo>`, and read your own
tenant type with `GetCurrentTenant<T>()`:

```csharp
app.MapGet("/tenant", (ITenantContext<string> tenants) => tenants.GetCurrentTenant<AppTenant>()?.Identifier);
```

`HasTenant` replaces `IsResolved`. Tenantry records no `StrategyInfo` or `StoreInfo`.

## 5. Move the EF Core setup

Finbuckle, with an interface on the context:

```csharp no-compile
public class AppDbContext : DbContext, IMultiTenantDbContext
{
    public AppDbContext(IMultiTenantContextAccessor accessor, DbContextOptions<AppDbContext> options)
        : base(options) => TenantInfo = accessor.MultiTenantContext.TenantInfo;

    public ITenantInfo? TenantInfo { get; }
    public TenantMismatchMode TenantMismatchMode => TenantMismatchMode.Throw;
    public TenantNotSetMode TenantNotSetMode => TenantNotSetMode.Throw;
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasIndex(o => o.Reference).IsUnique();
        modelBuilder.Entity<Order>().IsMultiTenant().AdjustUniqueIndexes();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.EnforceMultiTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // SaveChangesAsync calls EnforceMultiTenant() too.
}
```

Tenantry, with `UseTenantry()` where the context is registered (step 4):

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

public class Order : TenantEntity<string>
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The index AdjustUniqueIndexes() added TenantId to, with the name and columns it has now.
        modelBuilder.Entity<Order>().HasIndex(o => new { o.Reference, o.TenantId }).IsUnique()
            .HasDatabaseName("IX_Orders_Reference");
    }
}
```

The context loses its constructor parameter, its three properties and its `SaveChanges` overrides. A context derived
from Finbuckle's base class derives from `DbContext` instead, with the same constructor change.

### The TenantId column

Finbuckle adds `TenantId` as a shadow property, or uses an existing `string TenantId` property. Tenantry needs a public
property, which `TenantEntity<string>` provides. An entity that already has one implements `ITenantEntity<string>`
instead, and its setter can be private. Either maps to the column Finbuckle created, as a required string.

Finbuckle's `AdjustKey`, `AdjustIndex`, `AdjustIndexes` and `AdjustUniqueIndexes` add `TenantId` to keys and indexes;
Tenantry adds none. Declare them yourself with their current names and columns, as above. Otherwise the next migration
drops `TenantId` from them, and a unique index becomes unique across every tenant.

When `TenantId` is part of the primary key, set it to `ITenantContext<string>.CurrentTenantId` before `Add`, as
Finbuckle's `EnforceMultiTenantOnTracking` did. EF Core cannot track an entity whose key is null, and Tenantry stamps
`TenantId` only when saving.

Then add a migration. It should contain no operations:

```bash
dotnet ef migrations add MoveToTenantry
```

The model snapshot still changes, as Tenantry makes `TenantId` a concurrency token, which changes no column. If
the migration makes `TenantId` nullable, your project does not use nullable reference types: add
`modelBuilder.Entity<Order>().Property(o => o.TenantId).IsRequired()`.

### The rest of the context's code

- Drop the placeholder tenant from your design-time factory. `dotnet ef` builds the model with no tenant current
  ([Migrations](efcore-integration.md#migrations)).
- Code that ignored Finbuckle's named filter (`Constants.TenantToken`) names `TenantryQueryFilters.Tenant` instead.
- Code that created a context per tenant with Finbuckle's static `Create` method uses
  `ITenantScopeFactory<string>.CreateScope(tenant)` and resolves the context from the scope. Like `Create`, it trusts
  the tenant it is given. With only an id, use `RunInScopeAsync`, which looks it up
  ([Running work as a tenant](non-http-hosts.md#running-work-as-a-tenant)).
- A connection string on the tenant, read in `OnConfiguring`, becomes `UseConnectionStrings` with
  `AddDbContextPerTenantDatabase` ([Database per tenant](efcore-integration.md#database-per-tenant)).
- Code that relied on `TenantMismatchMode` or `TenantNotSetMode` to write across tenants moves to a maintenance context,
  and tests that expected an exception with no tenant now expect empty results
  ([Behaviour that changes](#behaviour-that-changes)).

## 6. Move per-tenant options

`services.ConfigurePerTenant<TOptions, TTenantInfo>((options, tenantInfo) => …)` becomes
`tenant.ConfigurePerTenant(perTenant => perTenant.Configure<TOptions>((options, t) => …))` inside `AddTenantry`:

- Read your tenant type with `t.As<AppTenant>()`.
- The named variants become `Configure<TOptions>(name, …)` and `ConfigureAll<TOptions>(…)` on the same builder.
- Finbuckle's `Reset()` and `Clear(tenantId)` become `ITenantInvalidator<string>.InvalidateAsync(tenantId)`.
- Code that reads the tenant's value through `IOptions<TOptions>` changes to `IOptionsSnapshot<TOptions>`, or
  `IOptionsMonitor<TOptions>` in a singleton. In Tenantry, `IOptions<TOptions>` keeps the ordinary value
  ([Options per tenant](per-tenant-options.md)).

`WithPerTenantAuthentication()` becomes `ConfigurePerTenant` on each scheme's options, such as
`Configure<OpenIdConnectOptions>("oidc", (o, t) => o.Authority = …)`, with the pipeline in
[step 4](#4-register-tenantry-and-its-middleware). A tenant's own challenge scheme becomes a policy scheme that forwards
to it ([A scheme per tenant](authentication-per-tenant.md#a-scheme-per-tenant)).

Finbuckle also refused a cookie signed in under another tenant. To keep that check, add the tenant id as a claim when
the user signs in, and validate it with `ValidateTenantAccessByClaim`
([Cookies](authentication-per-tenant.md#cookies)). A request for another tenant then gets `403` on every endpoint
there, where Finbuckle treated the user as signed out. To keep the user anonymous on the other tenant, as under
Finbuckle, give each tenant its own cookie name. Sessions signed in before the change lack the claim, so their users
must sign in again.

## Behaviour that changes

| | Finbuckle | Tenantry |
|-|-----------|----------|
| Forged keys | With `EnforceMultiTenantOnTracking`, an entity attached in tenant B's context with tenant A's key gets B's `TenantId` and passes, so saving it overwrites A's row and moves it to B. Without it, the save is refused for a `TenantId` that is not set under `TenantNotSetMode.Throw`, and moves the row under `Overwrite`. | The stored `TenantId` is in every `UPDATE` and `DELETE`, so the same save matches no row and throws `DbUpdateConcurrencyException`. An entity whose `TenantId` is not set is refused with `TenantIsolationViolationException` before anything is written. |
| Reading with no tenant | The query filter throws `NullReferenceException`. | Queries match no rows. Code and tests that expected the exception get empty results. |
| Writing with no tenant | Throws `MultiTenantException`. | Throws `TenantNotResolvedException`. One context can allow it ([`OnMissingTenant`](efcore-integration.md#onmissingtenant-writes-with-no-tenant)). |
| A write that names another tenant | `TenantMismatchMode` can throw, or with `Overwrite` and `Ignore`, replace or keep the other tenant's id. With `Overwrite`, an entity attached with another tenant's key and `TenantId` overwrites that tenant's row and moves it to the current tenant. | Throws `TenantIsolationViolationException` before anything is written. Maintenance code that writes across tenants uses a context of its own with `OnMissingTenant = Allow` and no tenant current. |
| An `ExecuteUpdate` that sets `TenantId` | Not checked: the current tenant's rows move to the tenant it sets. | Throws `TenantIsolationViolationException` when the query is compiled. |
| When the context reads the tenant | In its constructor, and keeps it. | On each query and save, so pooled contexts work. A database-per-tenant context is connected to one tenant's database, and throws instead after a switch. Use a context for one tenant: after a switch, `Find` and `Local` can return entities loaded for the previous one. |
| An identifier no store knows | The next strategy is tried. | The first identifier a resolver returns is used. If the store does not know it, the request has no tenant. |
| Identifier case | The in-memory and configuration stores match identifiers in any case. | The default lookup compares `string` ids exactly, and the subdomain and host resolvers return lower case. Ignore case in your `FindByIdentifierAsync` if clients send mixed case. |

Tenantry also refuses to build a model that cannot be isolated, for example one with a tenant-owned type whose base
type is not tenant-owned ([the list](efcore-advanced.md#models-that-cannot-be-isolated)).

## What Tenantry does not have

- Tenantry ships no database or configuration store; the in-memory store is for tests and samples. An application has
  one store. Finbuckle's configuration, distributed cache, HTTP remote and echo stores have no equivalent, and neither
  have its methods that add, update and remove tenants. Write an `ITenantStore<TKey>` as in step 3: two methods, and
  `FindByIdentifierAsync` when identifiers differ from ids.
- There is no base path, session, static or remote authentication callback resolver. Write an `ITenantResolver`
  ([Custom resolvers](tenant-resolution.md#custom-resolvers)). For a tenant in the path, use a route template with
  `{tenant}` and `ResolveFromRouteValue()`; nothing rewrites `PathBase`.
- `BypassWhen` and `BypassWhenEndpointNotResolved` have no equivalent. `UseTenantry()` resolves every request that
  reaches it, so put middleware that must skip it, such as `UseStaticFiles()`, before it. Assets that
  `MapStaticAssets()` maps are endpoints, which it reaches ([Static files](authentication-per-tenant.md#static-files)).
- `IgnoredIdentifiers` has no equivalent: the subdomain resolver ignores the subdomains in `IgnoredSubdomains`, and a
  store can return `null` for any other identifier.

