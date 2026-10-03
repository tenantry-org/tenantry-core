# Getting started

This guide takes you from an empty project to a working multi-tenant ASP.NET Core app with EF Core
data isolation. If you are not using ASP.NET Core, read [Non-HTTP hosts](non-http-hosts.md) after
the first two sections.

## 1. Install the packages

For an ASP.NET Core app backed by EF Core:

```bash
dotnet add package Tenantry.AspNetCore
dotnet add package Tenantry.EfCore
```

`Tenantry.Core` comes in transitively; reference it directly only if you want the core types in a
project that has neither of the above.

Tenantry targets **.NET 8, 9, and 10**. EF Core integration requires the matching major version of
`Microsoft.EntityFrameworkCore` (8.x, 9.x, or 10.x).

## 2. Choose your tenant key type

Tenantry's tenant types are generic over `TKey`, the type of your tenant identifier. `TKey` must implement
both `IEquatable<TKey>` (so EF Core can translate equality to SQL) and `IParsable<TKey>` (so Tenantry can
parse the id a request carries in a header, route, etc.).

`Guid`, `int`, `long`, and `string` all qualify out of the box. Pick one and use it consistently —
it appears in your entities, your store and your registration.

This guide uses `Guid`.

## 3. Register Tenantry

Registration needs no Tenantry `using` directive: `AddTenantry` and its builder methods are extension methods in
`Microsoft.Extensions.DependencyInjection`, and `UseTenantry`, `RequireTenant` and `AllowMissingTenant` in
`Microsoft.AspNetCore.Builder`. Types such as `TenantDescriptor<TKey>` are in the `Tenantry` namespace.

```csharp
using Microsoft.EntityFrameworkCore;
using Tenantry;

var builder = WebApplication.CreateBuilder(args);

// Introductory setup: any caller can select any tenant with the header. For production, add
// authentication and tenant access validation (see access-control.md and the SecureApi sample).
builder.Services.AddTenantry<Guid>(tenant =>
{
    // (a) Resolution — how the tenant is identified on each request.
    tenant.ResolveFromHeader("X-Tenant-Id");

    // (b) Storage — which tenants exist. Replace with a DB-backed store in production.
    tenant.UseInMemoryStore(
    [
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Acme" },
        new TenantDescriptor<Guid> { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Globex" },
    ]);
});
```

Builder methods chain, so this can also be one chain:
`tenant => tenant.ResolveFromHeader("X-Tenant-Id").UseInMemoryStore(tenants)`. `app.UseTenantry()` (step 7) throws
at startup if no resolver or no store is registered. The rules are in [Registration](core-concepts.md#registration).

## 4. Mark your tenant-owned entities

An entity becomes tenant-scoped by implementing `ITenantEntity<TKey>`. The convenience base class
`TenantEntity<TKey>` implements it for you:

```csharp
using Tenantry;

public class Order : TenantEntity<Guid>   // adds a `Guid TenantId { get; set; }` property
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}
```

Entities that do **not** implement `ITenantEntity<TKey>` are treated as global/shared data (product
catalogues, reference tables) and are never filtered or stamped. You never set `TenantId` yourself —
Tenantry stamps it on insert, and rejects a new entity that already names another tenant. The interface needs
only a getter, so an entity can implement it with a private or init-only setter.

## 5. Keep your DbContext as it is

Your `DbContext` needs no base class, no interface and no Tenantry calls. Configure it as you normally would,
in any order, including your own query filters:

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every query filters on TenantId, so lead your indexes with it.
        modelBuilder.Entity<Order>().HasIndex(o => new { o.TenantId, o.Id });
    }
}
```

## 6. Register the DbContext with `UseTenantry()`

`UseTenantry()` isolates the context: it adds the tenant query filter to every `ITenantEntity<Guid>` entity after
`OnModelCreating`, and attaches the interceptor that stamps and checks `TenantId` on `SaveChanges`. It works the
same with `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory`.

```csharp
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseTenantry());
```

## 7. Add the middleware

```csharp
var app = builder.Build();

app.UseTenantry();   // resolves the tenant; place after UseAuthentication() if resolving from claims
```

`UseTenantry()` must run **before** any endpoint that needs a tenant. With `WebApplication`, routing
is added automatically, so endpoint-level `RequireTenant()`/`AllowMissingTenant()` metadata is
respected. See [ASP.NET Core integration](aspnetcore-integration.md#pipeline-ordering) for ordering rules.

## 8. Use the tenant in your endpoints

```csharp
app.MapGet("/orders", async (AppDbContext db) =>
        // No Where(o => o.TenantId == …) needed — the global filter applies automatically.
        await db.Orders.ToListAsync())
   .RequireTenant();

app.MapPost("/orders", async (AppDbContext db, string description) =>
{
    db.Orders.Add(new Order { Description = description }); // TenantId stamped on save
    await db.SaveChangesAsync();
    return Results.Created();
}).RequireTenant();

app.Run();
```

## 9. Try it

```bash
# Acme creates and lists an order
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" -X POST "…/orders?description=Widgets"
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" "…/orders"   # shows the order

# Globex sees none of Acme's data
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000002" "…/orders"   # empty

# Missing/unknown/invalid tenant
curl "…/orders"                                                          # 400 (RequireTenant)
curl -H "X-Tenant-Id: not-a-guid" "…/orders"                             # 404 (names no tenant)
curl -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000099" "…/orders"  # 404 (not in store)
```

The rejections have an empty body. Add `builder.Services.AddProblemDetails()` to get `application/problem+json`
bodies instead; see [ASP.NET Core integration](aspnetcore-integration.md#status-codes).

## Next steps

- Replace the in-memory store with a real one — [Tenant stores](tenant-stores.md).
- Resolve tenants from subdomains, routes, or claims — [Tenant resolution](tenant-resolution.md).
- Restrict which users may access which tenants — [Access control](access-control.md).
- Understand the isolation policy, admin queries, and migrations — [EF Core integration](efcore-integration.md).
