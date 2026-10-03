# Migrating from Finbuckle.MultiTenant

This guide moves an ASP.NET Core application with EF Core from Finbuckle.MultiTenant 10.1.2 to Tenantry. Work
through the numbered steps in order. The sections after them list the behaviour that changes and what Tenantry does
not have.

## How the concepts map

| Finbuckle.MultiTenant | Tenantry |
|-----------------------|----------|
| `ITenantInfo`, `TenantInfo` (`Id`, `Identifier`, `Name`) | `ITenantDescriptor<TKey>`, `TenantDescriptor<TKey>` (`TenantId`, `Name`) |
| `AddMultiTenant<TTenantInfo>()` | `AddTenantry<TKey>(tenant => …)`, generic over the key type |
| Strategies: `WithHeaderStrategy()`, `WithHostStrategy()`, … | Resolvers: `ResolveFromHeader(…)`, `ResolveFromSubdomain()`, … |
| One or more stores: `WithInMemoryStore()`, `WithEFCoreStore<…>()`, … | One `ITenantStore<TKey>`: `UseInMemoryStore(…)` or `UseStore<TStore>()` |
| `UseMultiTenant()`, before `UseAuthentication()` | `UseTenantry()`, after `UseAuthentication()`; with per-tenant authentication, `UseTenantResolution()` before it too |
| `IMultiTenantContextAccessor<TTenantInfo>`, `HttpContext.GetMultiTenantContext<TTenantInfo>()` | `ITenantContext<TKey>` |
| `IMultiTenantContextSetter`, `HttpContext.SetTenantInfo(…)` | `ITenantContextSetter<TKey>.Use(tenant)`, or `ITenantScopeFactory<TKey>` for a new DI scope |
| Finbuckle's base context class, or `IMultiTenantDbContext` with `EnforceMultiTenant()` | A plain `DbContext` registered with `options.UseTenantry()` |
| `[MultiTenant]`, `IsMultiTenant()` | `ITenantEntity<TKey>` or `TenantEntity<TKey>` on the entity |
| `IsNotMultiTenant()` | Nothing: an entity without `ITenantEntity<TKey>` is shared |
| `TenantMismatchMode` | No setting: a write to another tenant's row always throws |
| `TenantNotSetMode` | No setting: a new entity is stamped, a changed one without the tenant throws |
| `ConfigurePerTenant<TOptions, TTenantInfo>(…)`, its named and `ConfigureAllPerTenant` variants | `ConfigurePerTenant(perTenant => perTenant.Configure<TOptions>(…))`, with `Configure<TOptions>(name, …)` and `ConfigureAll<TOptions>(…)`, in `AddTenantry` (Tenantry.Options) |
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

Add `Tenantry.Options` if you configure options per tenant. If you use
`Finbuckle.MultiTenant.Identity.EntityFrameworkCore`, remove it too, and set Identity up as in
[ASP.NET Core Identity](aspnetcore-identity.md).

## 2. Keep string tenant ids

Finbuckle's tenant ids are strings, and so is the `TenantId` column it adds to your tables. Register Tenantry with
`string` keys and that column and its data stay as they are.

To move to `Guid` or `int` keys, change the key type on your tenant type, your entities and `AddTenantry`, then add a
migration. The `AlterColumn` that EF Core generates for each `TenantId` column leaves converting the stored ids to the
database. Check that your database converts them, or convert them yourself in the migration with
`migrationBuilder.Sql`. Tenant ids stored anywhere else, such as claims in issued tokens, change too.

## 3. Move the tenant type and store

Finbuckle's strategies return an identifier, and the store finds the tenant with that `Identifier`. Tenantry's
resolvers return an identifier too, and the store's `FindByIdentifierAsync` finds the tenant. By default it reads the
identifier as the tenant id. When your identifiers differ from your ids, keep `Identifier` on your own tenant type and
look it up there:

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
in-memory store only where each identifier equals its id. To avoid a database round trip on every request, add
`CacheTenants()` ([Caching](tenant-stores.md#caching)).

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
app.UseTenantry();
app.UseAuthorization();
```

`UseTenantry()` goes after `UseAuthentication()`. Finbuckle's claim strategy authenticated the request itself.
`ResolveFromClaim` reads `HttpContext.User`, which the authentication middleware sets
([Pipeline ordering](aspnetcore-integration.md#pipeline-ordering)). If your authentication settings differ per tenant
(Finbuckle's `WithPerTenantAuthentication()`), also call `UseTenantResolution()` before `UseAuthentication()`, where
`UseMultiTenant()` was (step 6).

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

Finbuckle's header, claim and route value are named `__tenant__` by default. Tenantry's header resolver needs a name.
Its claim defaults to `tenant_id` and its route value to `tenant`. Pass the names your clients already send.

Finbuckle's default host template takes the first label of any host. Without base domains, `ResolveFromSubdomain()`
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

The context loses its constructor parameter, its three properties and its `SaveChanges` overrides. A context that
derived from Finbuckle's base class derives from `DbContext` instead, with the same constructor change.

### The TenantId column

Finbuckle adds `TenantId` as a shadow property, or uses an existing `string TenantId` property. Tenantry needs a public
property, which `TenantEntity<string>` provides. An entity that already has one implements `ITenantEntity<string>`
instead, and its setter can be private. Either maps to the column Finbuckle created, as a required string.

Finbuckle's `AdjustKey`, `AdjustIndex`, `AdjustIndexes` and `AdjustUniqueIndexes` add `TenantId` to keys and indexes.
Tenantry adds none. Declare them yourself with their current names and columns, as above. Otherwise the next migration
drops `TenantId` from them, and a unique index becomes unique across every tenant.

When `TenantId` is part of the primary key, set it to `ITenantContext<string>.CurrentTenantId` before `Add`. EF Core
cannot track an entity whose key is null, and Tenantry stamps `TenantId` only when saving. Finbuckle's
`EnforceMultiTenantOnTracking` did this for you.

Then add a migration. It should contain no operations:

```bash
dotnet ef migrations add MoveToTenantry
```

The model snapshot still changes, because Tenantry makes `TenantId` a concurrency token, which changes no column. If
the migration makes `TenantId` nullable, your project does not use nullable reference types: add
`modelBuilder.Entity<Order>().Property(o => o.TenantId).IsRequired()`.

### The rest of the context's code

- Drop the placeholder tenant from your design-time factory. `dotnet ef` builds the model with no tenant current
  ([Migrations](efcore-integration.md#migrations)).
- Code that ignored Finbuckle's named filter (`Constants.TenantToken`) names `TenantryQueryFilters.Tenant` instead.
- Code that created a context per tenant with Finbuckle's static `Create` method uses
  `ITenantScopeFactory<string>.CreateScope(tenant)` and resolves the context from the scope
  ([Non-HTTP hosts](non-http-hosts.md)).
- A connection string on the tenant, read in `OnConfiguring`, becomes `UseConnectionStrings` with
  `AddDbContextPerTenantDatabase` ([Database per tenant](efcore-integration.md#database-per-tenant)).

## 6. Move per-tenant options

`services.ConfigurePerTenant<TOptions, TTenantInfo>((options, tenantInfo) => …)` becomes
`tenant.ConfigurePerTenant(perTenant => perTenant.Configure<TOptions>((options, t) => …))` inside `AddTenantry`. Read
your tenant type with `t.As<AppTenant>()`. Finbuckle's `Reset()` and `Clear(tenantId)` become
`ITenantInvalidator<string>.InvalidateAsync(tenantId)`. The named variants become `Configure<TOptions>(name, …)` and
`ConfigureAll<TOptions>(…)` on the same builder. Code that reads the tenant's value through `IOptions<TOptions>` changes to `IOptionsSnapshot<TOptions>`, or `IOptionsMonitor<TOptions>` in a
singleton: in Tenantry, `IOptions<TOptions>` keeps the ordinary value. See [Options per tenant](per-tenant-options.md).

`WithPerTenantAuthentication()` becomes `ConfigurePerTenant` on each scheme's options, such as
`Configure<OpenIdConnectOptions>("oidc", (o, t) => o.Authority = …)`, and `app.UseTenantResolution()` before
`app.UseAuthentication()`, so the tenant is known when the scheme authenticates. A tenant's own challenge scheme
becomes a policy scheme that forwards to it ([A scheme per tenant](authentication-per-tenant.md#a-scheme-per-tenant)). Finbuckle also refused a cookie signed
in under another tenant. To keep that check, add the tenant id as a claim when the user signs in, and validate it with
`ValidateTenantAccessByClaim`. A request for another tenant then gets `403` where a tenant is required; Finbuckle
treated the same user as signed out. Sessions signed in before the change lack the claim, so their users sign in
again. See [Authentication per tenant](authentication-per-tenant.md).

## Behaviour that changes

- **Forged keys.** Finbuckle checks the `TenantId` an entity carries. With `EnforceMultiTenantOnTracking`, an entity
  attached in tenant B's context with tenant A's key gets B's `TenantId` and passes. Saving it overwrites A's row and
  moves it to B. Tenantry puts the stored `TenantId` in every `UPDATE` and `DELETE`, so the same save matches no row
  and throws `DbUpdateConcurrencyException`.
- **Reading with no tenant.** Finbuckle's query filter throws `NullReferenceException` when the context has no
  tenant. Tenantry's matches no rows. Code and tests that expected the exception get empty results.
- **Writing with no tenant.** Both refuse: Finbuckle throws `MultiTenantException`, Tenantry
  `TenantNotResolvedException`. Tenantry can allow it for one context
  ([`OnMissingTenant`](efcore-integration.md#onmissingtenant-writes-with-no-tenant)).
- **Mismatched tenants.** `TenantMismatchMode.Overwrite` and `Ignore` have no equivalent. A write that names another
  tenant throws `TenantIsolationViolationException`. Maintenance code that writes across tenants uses a context of its
  own with `OnMissingTenant = Allow` and no tenant current.
- **Bulk updates.** An `ExecuteUpdate` that sets `TenantId` throws. Finbuckle has no such check.
- **When the context reads the tenant.** Finbuckle's context takes the tenant in its constructor and keeps it.
  Tenantry's reads the current tenant on each query and save, so pooled contexts work. Use a context for one tenant:
  after a switch, `Find` and `Local` can return entities loaded for the previous one.
- **Models Tenantry refuses.** Building a model that cannot be isolated throws, for example a tenant-owned type
  whose base type is not tenant-owned ([the list](efcore-advanced.md#models-that-cannot-be-isolated)).
- **No fallback between resolvers.** Finbuckle tries the next strategy when no store knows an identifier. Tenantry
  uses the first identifier a resolver returns. If the store does not know it, the request has no tenant.
- **Identifier case.** Finbuckle's in-memory and configuration stores match identifiers in any case. Tenantry's
  default lookup compares `string` ids exactly. The subdomain and host resolvers return lower case. Ignore case in
  your `FindByIdentifierAsync` if clients send mixed case.

## What Tenantry does not have

- **Other stores.** Tenantry builds in only the in-memory store, and an application has one store. Finbuckle's
  configuration, distributed cache, HTTP remote and echo stores have no equivalent, and neither have its methods that
  add, update and remove tenants. Write an `ITenantStore<TKey>` as in step 3.
- **Other strategies.** There is no base path, session, static or remote authentication callback resolver. Write an
  `ITenantResolver` ([Custom resolvers](tenant-resolution.md#custom-resolvers)). For a tenant in the path, use a
  route template with `{tenant}` and `ResolveFromRouteValue()`; nothing rewrites `PathBase`.
- **Bypassing resolution.** `BypassWhen` and `BypassWhenEndpointNotResolved` have no equivalent. `UseTenantry()`
  resolves every request that reaches it, so put middleware that must skip it, such as static files, before it.
- **Ignored identifiers.** `IgnoredIdentifiers` has no equivalent. The subdomain resolver ignores the subdomains in
  `IgnoredSubdomains`, and a store can return `null` for any other identifier.

## Checklist

1. No Finbuckle package or `using` directive is left.
2. Tenantry is registered with `string` keys, or a key type change has a migration that converts the data.
3. Your tenant type implements `ITenantDescriptor<string>`, and the store maps identifiers in `FindByIdentifierAsync`.
4. The resolvers read the header, claim or route value your clients already send.
5. `UseTenantry()` runs after `UseAuthentication()`, and, with per-tenant authentication, `UseTenantResolution()`
   before it.
6. Tenant-owned entities implement `ITenantEntity<string>`, and each context is a plain `DbContext` registered with
   `UseTenantry()`.
7. Keys and indexes that Finbuckle adjusted are declared, and the new migration has no operations.
8. Ignored query filters name `TenantryQueryFilters.Tenant`.
9. Code that relied on `TenantMismatchMode` or `TenantNotSetMode` uses a maintenance context.
10. Per-tenant options use `ConfigurePerTenant`, per-tenant authentication names its scheme, and per-tenant
    sign-in is checked with a tenant claim.
11. Tests that expected an exception without a tenant expect empty results.
